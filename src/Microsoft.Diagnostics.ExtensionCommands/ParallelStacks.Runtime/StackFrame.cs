// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace ParallelStacks
{
    public class StackFrame
    {
        public string TypeName { get; private set; }
        public string MethodName { get; private set; }
        public string Text { get; private set; }
        public List<string> Signature { get; } = new List<string>();

        public StackFrame(ClrStackFrame frame)
        {
            ComputeNames(frame);
        }

        private void ComputeNames(ClrStackFrame frame)
        {
            // start by parsing (short)type name
            var typeName = frame.Method?.Type.Name;
            if (string.IsNullOrEmpty(typeName))
            {
                // IL generated frames
                TypeName = string.Empty;
            }
            else
            {
                TypeName = typeName;
            }

            // generic methods are not well formatted by ClrMD
            // foo<...>()  =>   foo[[...]]()
            var signature = frame.Method?.Signature;
            if (string.IsNullOrEmpty(signature))
            {
                Text = "?";
            }
            else
            {
                Text = string.Intern(signature);
                Signature.AddRange(BuildSignature(signature));
            }

            var methodName = frame.Method?.Name;
            if (string.IsNullOrEmpty(methodName))
            {
                // IL generated frames
                MethodName = "";
            }
            else if (methodName.EndsWith("]]"))
            {
                // fix ClrMD bug with method name
                MethodName = GetGenericMethodName(signature);
            }
            else
            {
                MethodName = methodName;
            }
        }

        public static string GetShortTypeName(string typeName, int start, int end)
        {
            return GetNextTypeName(typeName, ref start, ref end);
        }

        // this helper is called in 2 situations to analyze a method signature parameter:
        //  - compute the next type in a generic definition
        //  - start from a full type name
        // in all cases:
        //  - end   = the index of the last character (could be far beyond the end of the next type name in case of generic)
        //  - start = first character of the type name
        public static string GetNextTypeName(string typeName, ref int start, ref int end)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();

            // need to make the difference between generic and non generic parameters
            //      *.Int32                                         --> Int32
            //      *.IList`1<*.String>                             --> IList + continue on *.String>
            //      *.IDictionary`2<*.Int32,*.IList`1<*.String>>    --> IDictionary + continue on *.Int32,*.IList`1<*.String>>
            //      *.Int32,*.IList`1<*.String>>                    --> Int32 + continue on *.IList`1<*.String>>
            //      *.Int32>                                        --> Int32
            //  1. look for generic 
            //  2. if not, look for , as separator of generic parameters
            var pos = typeName.IndexOf('`', start, end - start);
            var next = typeName.IndexOf(',', start, end - start);

            // simple case of 1 type name (with maybe no namespace)
            if ((pos == -1) && (next == -1))
            {
                AppendTypeNameWithoutNamespace(sb, typeName, start, end);

                // it was the last type name
                start = end;

                return sb.ToString();
            }

            // this is the last type
            if (next == -1)
            {
                // *.IList`1<...>,xxx
                // *.IList`1<xxx,...>
                return GetGenericTypeName(typeName, ref start, ref end);
            }

            // at least 1 type name (even before a generic type)
            if (pos == -1)
            {
                // *.Int32,xxx  with xxx could contain a generic 
                AppendTypeNameWithoutNamespace(sb, typeName, start, next-1);

                // skip this type
                start = next + 1;

                return sb.ToString();
            }

            // a generic type before another type or a generic type with more than 1 parameter
            if (pos < next)
            {
                // *.IList`1<...>,xxx
                // *.IList`1<xxx,...>
                return GetGenericTypeName(typeName, ref start, ref end);
            }

            // a non generic type before another type parameter
            // *.Int32,xxx
            AppendTypeNameWithoutNamespace(sb, typeName, start, next-1);

            // skip this type
            start = next + 1;

            return sb.ToString();
        }

        public static string GetGenericTypeName(string typeName, ref int start, ref int end)
        {
            // System.Collections.Generic.IList`1<System.Collections.Generic.IEnumerable`1<System.String>>
            // System.Collections.Generic.IDictionary`2<Int32,System.String>
            var sb = new StringBuilder();

            // look for ` to get the name and the count of generic parameters
            var pos = typeName.IndexOf('`', start, end - start);

            // build the name                                       V-- don't want ` in the name
            AppendTypeNameWithoutNamespace(sb, typeName, start, pos-1);
            sb.Append('<');

            // go to the first generic parameter
            start = typeName.IndexOf('<', pos, end - pos) + 1;

            // get each generic parameter
            while (start < end)
            {
                var genericParameter = GetNextTypeName(typeName, ref start, ref end);
                sb.Append(genericParameter);
                if (start < end)
                {
                    sb.Append(',');
                }
            }

            return sb.ToString();
        }

        public static void AppendTypeNameWithoutNamespace(StringBuilder sb, string typeName, int start, int end)
        {
            var pos = typeName.LastIndexOf('.', end, end - start);
            if (pos == -1)
            {   // no namespace
                sb.Append(typeName, start, end - start + 1);
            }
            else
            {
                // skip the namespace
                sb.Append(typeName, pos + 1, end - pos);
            }
        }

        public static IEnumerable<string> BuildSignature(string fullName)
        {
            // {namespace.}type.method[[]](..., ..., ...)
            var parameters = new List<string>();
            var pos = fullName.LastIndexOf('(');
            if (pos == -1)
            {
                return parameters;
            }

            // look for each parameter, one after the other
            int next = pos;
            string parameter = string.Empty;
            while (next != (fullName.Length - 1))
            {
                next = fullName.IndexOf(", ", pos);
                if (next == -1)
                {
                    next = fullName.IndexOf(')'); // should be the last character of the string
                    Debug.Assert(next == fullName.Length - 1);
                }

                //                             skip   .      ,
                parameter = GetParameter(fullName, pos + 1, next - 1);
                if (parameter != null)
                {
                    parameters.Add(parameter);
                }

                pos = next + 1;
            }

            return parameters;
        }

        public static string GetParameter(string fullName, int start, int end)
        {
            const string BYREF = " ByRef";
            //   ()  no parameter
            if (start >= end)
            {
                return null;
            }

            var sb = new StringBuilder();

            // handle ByRef case
            var isByRef = false;
            if (fullName.LastIndexOf(BYREF, end) == end - BYREF.Length)
            {
                isByRef = true;
                end -= BYREF.Length;
            }

            var typeName = GetShortTypeName(fullName, start, end);
            sb.Append(typeName);

            if (isByRef)
            {
                sb.Append(BYREF);
            }

            return sb.ToString();
        }

        public static string GetGenericMethodName(string fullName)
        {
            // foo[[...]] --> foo<...>
            // namespace.type.Foo[[System.String, Int32]](System.Collections.Generic.IDictionary`2<Int32,System.String>)
            var pos = fullName.IndexOf("[[");
            if (pos == -1)
            {
                return fullName;
            }

            var start = fullName.LastIndexOf('.', pos);
            return fullName.Substring(start + 1, pos - start - 1);
        }
    }
}
