// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public static class Extensions
    {
        public static void WriteString(this BinaryWriter @this, string value)
        {
            if (@this == null)
                throw new ArgumentNullException(nameof(@this));

            @this.Write(value != null ? (value.Length + 1) : 0);
            if (value != null)
                @this.Write(Encoding.Unicode.GetBytes(value + '\0'));
        }

#if DEBUG
        private static int GetByteCount(this string @this)
        {
            if (@this == null)
                throw new ArgumentNullException(nameof(@this));

            var strLength = @this == null ? 0 : Encoding.Unicode.GetByteCount(@this + '\0');
            return Marshal.SizeOf(typeof(int)) + strLength;
        }

        public static int GetByteCount(this SessionConfiguration @this)
        {
            int size = 0;

            size += Marshal.SizeOf(@this.CircularBufferSizeInMB.GetType());
            size += Marshal.SizeOf(typeof(int));

            foreach (var provider in @this.Providers)
            {
                size += Marshal.SizeOf(provider.Keywords.GetType());
                size += Marshal.SizeOf(typeof(uint)); // provider.EventLevel.GetType()
                size += provider.Name.GetByteCount();
                size += provider.FilterData.GetByteCount();
            }

            return size;
        }
#endif // DEBUG
    }
}
