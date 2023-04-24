// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal static class Dml
    {
        private static DmlDumpObject s_dumpObj;
        private static DmlBold s_bold;

        public static DmlFormat DumpObj { get => s_dumpObj ??= new DmlDumpObject(); }

        public static DmlFormat Bold { get => s_bold ??= new DmlBold(); }

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
                sb.Append("<exec cmd=\"");
                AppendCommand(sb, outputText, value);
                sb.Append('\"');

                string altText = GetAltText(outputText, value);
                if (altText is not null)
                {
                    sb.Append("alt=\"");
                    sb.Append(altText);
                    sb.Append('"');
                }

                sb.Append('>');
                sb.Append(DmlEscape(outputText));
                sb.Append("</exec>");
            }

            protected abstract void AppendCommand(StringBuilder sb, string outputText, object value);
            protected virtual string GetAltText(string outputText, object value) => null;
        }

        private sealed class DmlDumpObject : DmlExec
        {
            protected override void AppendCommand(StringBuilder sb, string outputText, object value)
            {
                value = Format.Unwrap(value);
                sb.Append("!dumpobj /d ");
                sb.AppendFormat("{0:x}", value);
            }

            protected override string GetAltText(string outputText, object value)
            {
                if (value is ClrObject obj)
                {
                    return obj.Type?.Name;
                }

                return null;
            }
        }
    }
}
