// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace Microsoft.FileFormats.Minidump
{
    internal static class MinidumpReaderExtensions
    {
        public static string ReadCountedString(this Reader self, ulong position, Encoding encoding)
        {
            uint elementCount = self.Read<uint>(ref position);
            byte[] buffer = self.Read(position, elementCount);
            return encoding.GetString(buffer);
        }

        public static T[] ReadCountedArray<T>(this Reader self, ulong position)
        {
            uint elementCount = self.Read<uint>(ref position);
            return (T[])self.LayoutManager.GetArrayLayout<T[]>(elementCount).Read(self.DataSource, position);
        }
    }
}
