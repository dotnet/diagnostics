// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal abstract class MinidumpMemoryReader : CommonMemoryReader, IDisposable
    {
        public abstract void Dispose();

        public abstract int ReadFromRva(ulong rva, Span<byte> buffer);

        public string? ReadCountedUnicode(ulong rva)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1024);
            try
            {
                int count;

                Span<byte> span = new(buffer);
                if (ReadFromRva(rva, span.Slice(0, sizeof(int))) != sizeof(int))
                    return null;

                int len = Unsafe.As<byte, int>(ref buffer[0]);
                len = Math.Min(len, buffer.Length);

                if (len <= 0)
                    return null;

                count = ReadFromRva(rva + sizeof(int), buffer);
                string result = Encoding.Unicode.GetString(buffer, 0, count);

                int index = result.IndexOf('\0');
                if (index != -1)
                    result = result.Substring(0, index);

                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}