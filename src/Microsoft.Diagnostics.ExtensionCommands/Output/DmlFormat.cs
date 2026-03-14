// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Xml.Linq;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal abstract class DmlFormat
    {
        // intentionally not shared with Format
        private static readonly StringBuilderPool s_stringBuilderPool = new();

        public virtual string FormatValue(string outputText, object value)
        {
            StringBuilder sb = s_stringBuilderPool.Rent();

            FormatValue(sb, outputText, value);
            string result = sb.ToString();
            s_stringBuilderPool.Return(sb);
            return result;
        }

        public abstract void FormatValue(StringBuilder sb, string outputText, object value);

        protected static string DmlEscape(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return new XText(text).ToString();
        }
    }
}
