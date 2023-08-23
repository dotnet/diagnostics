// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    public interface IMemoryReader
    {
        /// <summary>
        /// Gets the size of a pointer in the target process.
        /// </summary>
        /// <returns>The pointer size of the target process.</returns>
        int PointerSize { get; }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="address">The address of memory to read.</param>
        /// <param name="buffer">The buffer to write to.</param>
        /// <returns>The number of bytes read into the buffer.</returns>
        int Read(ulong address, Span<byte> buffer);

        /// <summary>
        /// Read an unmanaged value from the given address.
        /// </summary>
        /// <typeparam name="T">The type to read.  This may be a struct or primitive type as long as it does
        /// not managed pointers.</typeparam>
        /// <param name="address">The address to read from.</param>
        /// <param name="value">The value that was read.</param>
        /// <returns>True if the memory was present, false otherwise.</returns>
        bool Read<T>(ulong address, out T value) where T : unmanaged;

        /// <summary>
        /// Read an unmanaged value from the given address.  Returns default(T) if the data was not readable.
        /// </summary>
        /// <typeparam name="T">The type to read.  This may be a struct or primitive type as long as it does
        /// not managed pointers.</typeparam>
        /// <param name="address">The address to read from.</param>
        /// <returns>The value at addr, or default(T) if not present in the data target.</returns>
        T Read<T>(ulong address) where T : unmanaged;

        /// <summary>
        /// Reads a pointer at the given address.
        /// </summary>
        /// <param name="address">The address to read from.</param>
        /// <param name="value">A pointer sized value that was read.</param>
        /// <returns>True if the value was read, false if the value could not be read.</returns>
        bool ReadPointer(ulong address, out ulong value);

        /// <summary>
        /// Read a pointer out of the target process.
        /// </summary>
        /// <returns>
        /// The pointer at the give address, or 0 if that pointer doesn't exist in
        /// the data target.
        /// </returns>
        ulong ReadPointer(ulong address);
    }
}
