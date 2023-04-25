// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal static class Dml
    {
        private static DmlDumpObject s_dumpObj;
        private static DmlDumpHeapMT s_dumpHeapMT;
        private static DmlDumpHeapSegment s_dumpHeapSegment;
        private static DmlBold s_bold;
        private static DmlListNearObj s_listNearObj;
        private static DmlDumpDomain s_dumpDomain;

        public static DmlFormat DumpObj => s_dumpObj ??= new();
        public static DmlFormat Bold => s_bold ??= new();
        public static DmlFormat DumpHeapMT => s_dumpHeapMT ??= new();
        public static DmlFormat DumpHeapSegment => s_dumpHeapSegment ??= new();
        public static DmlFormat ListNearObj => s_listNearObj ??= new();
        public static DmlFormat DumpDomain => s_dumpDomain ??= new();

        private sealed class DmlBold : DmlFormat
        {
            public override void FormatValue(StringBuilder sb, string outputText, object value)
            {
                sb.Append("<b>");
                sb.Append(DmlEscape(outputText));
                sb.Append("</b>");
            }
        }

        private abstract class DmlExec : DmlFormat
        {
            public override void FormatValue(StringBuilder sb, string outputText, object value)
            {
                string command = GetCommand(outputText, value);
                if (string.IsNullOrWhiteSpace(command))
                {
                    sb.Append(DmlEscape(outputText));
                    return;
                }

                sb.Append("<exec cmd=\"");
                sb.Append(DmlEscape(command));
                sb.Append('\"');

                string altText = GetAltText(outputText, value);
                if (altText is not null)
                {
                    sb.Append(" alt=\"");
                    sb.Append(DmlEscape(altText));
                    sb.Append('"');
                }

                sb.Append('>');
                sb.Append(DmlEscape(outputText));
                sb.Append("</exec>");
            }

            protected abstract string GetCommand(string outputText, object value);
            protected virtual string GetAltText(string outputText, object value) => null;

            protected static bool IsNullOrZeroValue(object obj, out string value)
            {
                if (obj is null)
                {
                    value = null;
                    return true;
                }
                else if (TryGetPointerValue(obj, out ulong ul) && ul == 0)
                {
                    value = "0";
                    return true;
                }

                value = null;
                return false;
            }

            protected static bool TryGetPointerValue(object value, out ulong ulVal)
            {
                if (value is ulong ul)
                {
                    ulVal = ul;
                    return true;
                }
                else if (value is nint ni)
                {
                    unchecked
                    {
                        ulVal = (ulong)ni;
                    }
                    return true;
                }
                else if (value is nuint nuint)
                {
                    ulVal = nuint;
                    return true;
                }

                ulVal = 0;
                return false;
            }

        }

        private class DmlDumpObject : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                bool isValid = true;
                if (value is ClrObject obj)
                {
                    isValid = obj.IsValid;
                }

                value = Format.Unwrap(value);
                if (IsNullOrZeroValue(value, out string result))
                {
                    return result;
                }

                return isValid ? $"!dumpobj /d {value:x}" : $"!verifyobj {value:x}";
            }

            protected override string GetAltText(string outputText, object value)
            {
                if (value is ClrObject obj)
                {
                    if (obj.IsValid)
                    {
                        return obj.Type?.Name;
                    }

                    return "Invalid Object";
                }

                return null;
            }
        }

        private sealed class DmlListNearObj : DmlDumpObject
        {
            protected override string GetCommand(string outputText, object value)
            {
                value = Format.Unwrap(value);
                if (IsNullOrZeroValue(value, out string result))
                {
                    return result;
                }

                return $"!listnearobj {value:x}";
            }
        }

        private sealed class DmlDumpHeapSegment : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                value = Format.Unwrap(value);
                if (IsNullOrZeroValue(value, out string result))
                {
                    return result;
                }

                return $"!dumpheap -segment {value:x}";
            }
        }

        private sealed class DmlDumpHeapMT : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                value = Format.Unwrap(value);
                if (IsNullOrZeroValue(value, out string result))
                {
                    return result;
                }

                if (TryGetPointerValue(value, out ulong mtOrTh))
                {
                    // !dumpheap will only work on a method table
                    if ((mtOrTh & 2) == 2)
                    {
                        // Can't use typehandles
                        return null;
                    }
                    else if ((mtOrTh & 1) == 1)
                    {
                        // Clear mark bit
                        value = mtOrTh & ~1ul;
                    }
                }

                return $"!dumpheap -mt {value:x}";
            }

            protected override string GetAltText(string outputText, object value)
            {
                if (value is ClrType type)
                {
                    return type.Name;
                }

                return null;
            }
        }

        private sealed class DmlDumpDomain : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                value = Format.Unwrap(value);
                if (IsNullOrZeroValue(value, out string result))
                {
                    return result;
                }

                return $"!dumpdomain /d {value:x}";
            }

            protected override string GetAltText(string outputText, object value)
            {
                if (value is ClrAppDomain domain)
                {
                    return domain.Name;
                }

                return null;
            }
        }
    }
}
