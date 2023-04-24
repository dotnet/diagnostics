// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal static class Dml
    {
        private static DmlDumpObject s_dumpObj;
        private static DmlDumpHeapMT s_dumpHeapMT;
        private static DmlBold s_bold;
        private static DmlListNearObj s_listNearObj;

        public static DmlFormat DumpObj => s_dumpObj ??= new();
        public static DmlFormat Bold => s_bold ??= new();
        public static DmlFormat DumpHeapMT => s_dumpHeapMT ??= new();
        public static DmlFormat ListNearObj => s_listNearObj ??= new();

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
                    return;
                }

                sb.Append("<exec cmd=\"");
                sb.Append(DmlEscape(command));
                sb.Append('\"');

                string altText = GetAltText(outputText, value);
                if (altText is not null)
                {
                    sb.Append(" alt=\"");
                    sb.Append(altText);
                    sb.Append('"');
                }

                sb.Append('>');
                sb.Append(DmlEscape(outputText));
                sb.Append("</exec>");
            }

            protected abstract string GetCommand(string outputText, object value);
            protected virtual string GetAltText(string outputText, object value) => null;
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
                if (value is null)
                {
                    return null;
                }
                else if (value is ulong ul && ul == 0ul)
                {
                    return "0";
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
                if (value is null || (value is ulong ul && ul == 0ul))
                {
                    return null;
                }

                return $"!listnearobj {value:x}";
            }
        }

        private sealed class DmlDumpHeapMT : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                value = Format.Unwrap(value);
                if (value is null || (value is ulong ul && ul == 0ul))
                {
                    return null;
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
    }
}
