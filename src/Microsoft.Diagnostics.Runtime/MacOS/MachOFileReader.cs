// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal readonly struct MachOFileReader
    {
        private readonly Stream _stream;

        internal MachOFileReader(Stream stream)
        {
            _stream = stream;
        }

        internal int Read(long position, Span<byte> buffer)
        {
            _stream.Seek(position, SeekOrigin.Begin);
            return _stream.Read(buffer);
        }

        internal unsafe T Read<T>()
            where T : unmanaged
        {
            int size = sizeof(T);
            T result;
            if (_stream.Read(new Span<byte>(&result, size)) != size)
            {
                throw new IOException();
            }

            return result;
        }
    }
}
