// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace System.CommandLine
{
    // class copied from https://raw.githubusercontent.com/dotnet/command-line-api/060374e56c1b2e741b6525ca8417006efb54fbd7/src/System.CommandLine.DragonFruit/StringExtensions.cs
    internal static class StringExtensions
    {
        public static string ToKebabCase(this string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            StringBuilder sb = new();
            int i = 0;
            bool addDash = false;

            // handles beginning of string, breaks on first letter or digit. addDash might be better named "canAddDash"
            for (; i < value.Length; i++)
            {
                char ch = value[i];
                if (char.IsLetterOrDigit(ch))
                {
                    addDash = !char.IsUpper(ch);
                    sb.Append(char.ToLowerInvariant(ch));
                    i++;
                    break;
                }
            }

            // reusing i, start at the same place
            for (; i < value.Length; i++)
            {
                char ch = value[i];
                if (char.IsUpper(ch))
                {
                    if (addDash)
                    {
                        addDash = false;
                        sb.Append('-');
                    }

                    sb.Append(char.ToLowerInvariant(ch));
                }
                else if (char.IsLetterOrDigit(ch))
                {
                    addDash = true;
                    sb.Append(ch);
                }
                else //this coverts all non letter/digits to dash - specifically periods and underscores. Is this needed?
                {
                    addDash = false;
                    sb.Append('-');
                }
            }

            return sb.ToString();
        }
    }
}
