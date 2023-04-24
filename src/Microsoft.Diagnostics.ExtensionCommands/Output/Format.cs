// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands.Output
{
    internal class Format
    {
        private static StringBuilderPool s_stringBuilderPool = new();

        /// <summary>
        /// Returns true if a format of this type should never be truncated.  If true,
        /// DEBUG builds of SOS Assert.Fail if attempting to truncate the value of the
        /// column.  In release builds, we will simply not truncate the value, resulting
        /// in a jagged looking table, but usable output.
        /// </summary>
        public bool CanTruncate { get; protected set; }

        public Format() { }
        public Format(bool canTruncate) => CanTruncate = canTruncate;

        // Unwraps an object to get at what should be formatted.
        internal static object Unwrap(object value)
        {
            return value switch
            {
                ClrObject obj => obj.Address,
                ClrAppDomain domain => domain.Address,
                ClrType type => type.MethodTable,
                _ => value
            };
        }

        public virtual string FormatValue(object value, int maxLength, bool truncateBegin)
        {
            StringBuilder sb = s_stringBuilderPool.Rent();

            FormatValue(sb, value, maxLength, truncateBegin);
            string result = sb.ToString();

            s_stringBuilderPool.Return(sb);
            return TruncateString(result, maxLength, truncateBegin);
        }

        public virtual int FormatValue(StringBuilder sb, object value, int maxLength, bool truncateBegin)
        {
            int currLength = sb.Length;
            sb.Append(value);
            TruncateStringBuilder(sb, maxLength, sb.Length - currLength, truncateBegin);

            return sb.Length - currLength;
        }

        protected string TruncateString(string result, int maxLength, bool truncateBegin)
        {
            if (maxLength >= 0 && result.Length > maxLength)
            {
                if (CanTruncate)
                {
                    if (maxLength <= 3)
                    {
                        result = new string('.', maxLength);
                    }
                    else if (truncateBegin)
                    {
                        result = "..." + result.Substring(result.Length - (maxLength - 3));
                    }
                    else
                    {
                        result = result.Substring(0, maxLength - 3) + "...";
                    }
                }
                else
                {
                    Debug.Fail("Tried to truncate a column we should never truncate.");
                }
            }

            Debug.Assert(maxLength < 0 || result.Length <= maxLength);
            return result;
        }

        protected void TruncateStringBuilder(StringBuilder result, int maxLength, int lengthWritten, bool truncateBegin)
        {
            Debug.Assert(lengthWritten >= 0);

            if (maxLength >= 0 && lengthWritten > maxLength)
            {
                if (CanTruncate)
                {
                    if (truncateBegin)
                    {
                        int start = result.Length - lengthWritten;
                        int wrote;
                        for (wrote = 0; wrote < 3 && wrote < maxLength; wrote++)
                        {
                            result[start + wrote] = '.';
                        }

                        int gap = lengthWritten - maxLength;
                        for (; wrote < maxLength; wrote++)
                        {
                            result[wrote] = result[wrote + gap];
                        }

                        result.Length = maxLength;
                    }
                    else
                    {
                        result.Length = result.Length - lengthWritten + maxLength;
                        for (int i = 0; i < maxLength && i < 3; i++)
                        {
                            result[result.Length - i - 1] = '.';
                        }
                    }
                }
                else
                {
                    Debug.Fail("Tried to truncate a column we should never truncate.");
                }
            }
        }
    }
}
