// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
using Microsoft.SymbolStore.KeyGenerators;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class MemoryServiceExtensions
    {
        /// <summary>
        /// Returns the mask to remove any sign extension for 32 bit addresses
        /// </summary>
        public static ulong SignExtensionMask(this IMemoryService memoryService)
        {
            return memoryService.PointerSize == 4 ? uint.MaxValue : ulong.MaxValue;
        }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to write to</param>
        /// <param name="bytesRequested">The number of bytes to read</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public static bool ReadMemory(this IMemoryService memoryService, ulong address, byte[] buffer, int bytesRequested, out int bytesRead)
        {
            return memoryService.ReadMemory(address, new Span<byte>(buffer, 0, bytesRequested), out bytesRead);
        }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to read memory into</param>
        /// <param name="bytesRequested">The number of bytes to read</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public static bool ReadMemory(this IMemoryService memoryService, ulong address, IntPtr buffer, int bytesRequested, out int bytesRead)
        {
            unsafe
            {
                return memoryService.ReadMemory(address, new Span<byte>(buffer.ToPointer(), bytesRequested), out bytesRead);
            }
        }

        /// <summary>
        /// Read a 32 bit value from the memory location
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">address to read</param>
        /// <param name="value">returned value</param>
        /// <returns>true success, false failure</returns>
        public static bool ReadDword(this IMemoryService memoryService, ulong address, out uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            if (memoryService.ReadMemory(address, buffer, out int bytesRead))
            {
                if (bytesRead == sizeof(uint))
                {
                    value = MemoryMarshal.Read<uint>(buffer);
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Return a pointer sized value from the address.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">address to read</param>
        /// <param name="value">returned value</param>
        /// <returns>true success, false failure</returns>
        public static bool ReadPointer(this IMemoryService memoryService, ulong address, out ulong value)
        {
            int pointerSize = memoryService.PointerSize;
            Span<byte> buffer = stackalloc byte[pointerSize];
            if (memoryService.ReadMemory(address, buffer, out int bytesRead))
            {
                switch (pointerSize)
                {
                    case 4:
                        value = MemoryMarshal.Read<uint>(buffer);
                        return true;
                    case 8:
                        value = MemoryMarshal.Read<ulong>(buffer);
                        return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Create a stream for all of memory.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <returns>Stream of all of memory</returns>
        public static Stream CreateMemoryStream(this IMemoryService memoryService)
        {
            return new TargetStream(memoryService, 0, long.MaxValue);
        }

        /// <summary>
        /// Create a stream for the address range.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">address to read</param>
        /// <param name="size">size of stream</param>
        /// <returns>memory range Stream</returns>
        public static Stream CreateMemoryStream(this IMemoryService memoryService, ulong address, ulong size)
        {
            Debug.Assert(address != 0);
            Debug.Assert(size != 0);
            return new TargetStream(memoryService, address, size);
        }

        /// <summary>
        /// Stream implementation to read debugger target memory for in-memory PDBs
        /// </summary>
        class TargetStream : Stream
        {
            private readonly ulong _address;
            private readonly IMemoryService _memoryService;

            public override long Position { get; set; }
            public override long Length { get; }
            public override bool CanSeek { get { return true; } }
            public override bool CanRead { get { return true; } }
            public override bool CanWrite { get { return false; } }

            public TargetStream(IMemoryService memoryService, ulong address, ulong size)
                : base()
            {
                _memoryService = memoryService;
                _address = address;
                Length = (long)size;
                Position = 0;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Position + count > Length) {
                    return 0;
                }
                if (_memoryService.ReadMemory(_address + (ulong)Position, new Span<byte>(buffer, offset, count), out int bytesRead)) {
                    Position += bytesRead;
                }
                return bytesRead;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin) {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;
                    case SeekOrigin.End:
                        Position = Length + offset;
                        break;
                    case SeekOrigin.Current:
                        Position += offset;
                        break;
                }
                return Position;
            }

            public override void Flush()
            {
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }
    }
}
