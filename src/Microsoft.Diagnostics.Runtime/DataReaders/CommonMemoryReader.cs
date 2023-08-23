// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
    internal abstract class CommonMemoryReader : IMemoryReader
    {
        public virtual int PointerSize => IntPtr.Size;

        public abstract int Read(ulong address, Span<byte> buffer);

        public unsafe bool Read<T>(ulong address, out T value)
            where T : unmanaged
        {
            Span<byte> buffer = stackalloc byte[sizeof(T)];
            if (Read(address, buffer) == buffer.Length)
            {
                value = Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(buffer));
                return true;
            }

            value = default;
            return false;
        }

        public T Read<T>(ulong address)
            where T : unmanaged
        {
            Read(address, out T result);
            return result;
        }

        public bool ReadPointer(ulong address, out ulong value)
        {
            Span<byte> buffer = stackalloc byte[IntPtr.Size];
            if (Read(address, buffer) == IntPtr.Size)
            {
                value = buffer.AsPointer();
                return true;
            }

            value = 0;
            return false;
        }

        public ulong ReadPointer(ulong address)
        {
            ReadPointer(address, out ulong value);
            return value;
        }
    }
}
