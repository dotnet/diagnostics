using System;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    internal static class Extensions
    {
        public static void WriteString(this BinaryWriter @this, string value)
        {
            if (@this == null)
                throw new ArgumentNullException(nameof(@this));
            @this.Write(value != null ? (value.Length + 1) : 0);
            if (value != null)
                @this.Write(Encoding.Unicode.GetBytes(value + '\0'));
        }
    }
}
