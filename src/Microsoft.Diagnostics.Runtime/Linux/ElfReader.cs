// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal sealed unsafe class Reader
    {
        public IAddressSpace DataSource { get; }

        public Reader(IAddressSpace source)
        {
            DataSource = source;
        }

        public T? TryRead<T>(ulong position)
            where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            T result;
            int read = DataSource.Read(position, new Span<byte>(&result, size));
            if (read == size)
                return result;

            return null;
        }

        public T Read<T>(ulong position)
            where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            T result;
            int read = DataSource.Read(position, new Span<byte>(&result, size));
            if (read != size)
                throw new IOException();

            return result;
        }

        public T Read<T>(ref ulong position)
            where T : unmanaged
        {
            int size = Unsafe.SizeOf<T>();
            T result;
            int read = DataSource.Read(position, new Span<byte>(&result, size));
            if (read != size)
                throw new IOException();

            position += (uint)read;
            return result;
        }

        public int ReadBytes(ulong position, Span<byte> buffer) => DataSource.Read(position, buffer);

        public string ReadNullTerminatedAscii(ulong position)
        {
            StringBuilder builder = new(64);
            Span<byte> bytes = stackalloc byte[64];

            bool done = false;
            int read;
            while (!done && (read = DataSource.Read(position, bytes)) != 0)
            {
                position += (uint)read;
                for (int i = 0; !done && i < read; i++)
                {
                    if (bytes[i] != 0)
                        builder.Append((char)bytes[i]);
                    else
                        done = true;
                }
            }

            return builder.ToString();
        }

        public string ReadNullTerminatedAscii(ulong position, int length)
        {
            byte[]? array = null;
            Span<byte> buffer = length <= 32 ? stackalloc byte[length] : (array = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

            try
            {
                int read = DataSource.Read(position, buffer);
                if (read == 0)
                    return string.Empty;

                if (buffer[read - 1] == '\0')
                    read--;

                return Encoding.ASCII.GetString(buffer.Slice(0, read));
            }
            finally
            {
                if (array != null)
                    ArrayPool<byte>.Shared.Return(array);
            }
        }
    }
}