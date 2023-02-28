// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public sealed class MemoryCache
    {
        /// <summary>
        /// This class represents a chunk of cached memory, more or less a page.
        /// </summary>
        private class Cluster
        {
            internal const int Size = 4096;

            /// <summary>
            /// Empty cluster
            /// </summary>
            internal static Cluster Empty = new Cluster(Array.Empty<byte>());

            /// <summary>
            /// The cached data.
            /// </summary>
            private readonly byte[] _data;

            /// <summary>
            /// Length of cluster data
            /// </summary>
            internal int Length => _data.Length;

            /// <summary>
            /// Creates a cluster for some data.
            /// If the buffer is shorter than a page, it build a validity bitmap for it.
            /// </summary>
            /// <param name="data">the data to cache</param>
            ///
            internal Cluster(byte[] data)
            {
                _data = data;
            }

            /// <summary>
            /// Computes the base address of the cluster holding an address.
            /// </summary>
            /// <param name="address">input address</param>
            /// <returns>start address of the cluster</returns>
            internal static ulong GetBase(ulong address)
            {
                return address & ~(ulong)(Cluster.Size - 1);
            }

            /// <summary>
            /// Computes the offset of an address inside of the cluster.
            /// </summary>
            /// <param name="address">input address</param>
            /// <returns>offset of address</returns>
            internal static int GetOffset(ulong address)
            {
                return unchecked((int)((uint)address & (Cluster.Size - 1)));
            }

            /// <summary>
            /// Reads at up <paramref name="size"/> bytes from location <paramref name="address"/>.
            /// </summary>
            /// <param name="address">desired address</param>
            /// <param name="buffer">buffer to read</param>
            /// <param name="size">number of bytes to read</param>
            /// <returns>bytes read</returns>
            internal int ReadBlock(ulong address, Span<byte> buffer, int size)
            {
                int offset = GetOffset(address);
                if (offset < _data.Length)
                {
                    size = Math.Min(_data.Length - offset, size);
                    new Span<byte>(_data, offset, size).CopyTo(buffer);
                }
                else
                {
                    size = 0;
                }
                return size;
            }
        }

        /// <summary>
        /// After memory cache reaches the limit size, it gets flushed upon next access.
        /// </summary>
        private const int CacheSizeLimit = 64 * 1024 * 1024; // 64 MB

        /// <summary>
        /// The delegate to the actual read memory
        /// </summary>
        public delegate byte[] ReadMemoryDelegate(ulong address, int size);

        private readonly Dictionary<ulong, Cluster> _map;
        private readonly ReadMemoryDelegate _readMemory;

        public MemoryCache(ReadMemoryDelegate readMemory)
        {
            _map = new Dictionary<ulong, Cluster>();
            _readMemory = readMemory;
        }

        /// <summary>
        /// Current size of this memory cache
        /// </summary>
        public long CacheSize { get; private set; }

        /// <summary>
        /// Flush this memory cache
        /// </summary>
        public void FlushCache()
        {
            Trace.TraceInformation("Flushed memory cache");
            _map.Clear();
            CacheSize = 0;
        }

        /// <summary>
        /// Reads up to <paramref name="buffer.Length"/> bytes of memory at <paramref name="address"/>.
        /// It walks the set of clusters to collect as much data as possible.
        /// </summary>
        /// <param name="address">address to read</param>
        /// <param name="buffer">span of buffer to read memory</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if read memory succeeded or partially succeeded</returns>
        public bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead)
        {
            int bytesRequested = buffer.Length;
            int offset = 0;

            while (bytesRequested > 0)
            {
                Cluster cluster = GetCluster(address);
                int read = cluster.ReadBlock(address, buffer.Slice(offset), bytesRequested);
                if (read <= 0)
                {
                    break;
                }
                address += (uint)read;
                offset += read;
                bytesRequested -= read;
            }

            bytesRead = offset;
            return offset > 0;
        }

        /// <summary>
        /// Ensures that an address is cached.
        /// </summary>
        /// <param name="address">target address</param>
        /// <returns>It will resolve to an existing cluster or a newly-created one</returns>
        private Cluster GetCluster(ulong address)
        {
            ulong baseAddress = Cluster.GetBase(address);

            if (!_map.TryGetValue(baseAddress, out Cluster cluster))
            {
                if (CacheSize >= CacheSizeLimit)
                {
                    FlushCache();
                }

                // There are 3 things that can happen here:
                // 1) Normal full size cluster read (== Cluster.Size). The full block memory is cached.
                // 2) Partial cluster read (< Cluster.Size). The partial memory block is cached and the memory after it is invalid.
                // 3) Data == null. Read failure. Failure is cached.
                byte[] data = _readMemory(baseAddress, Cluster.Size);

                cluster = data == null ? Cluster.Empty : new Cluster(data);
                CacheSize += cluster.Length;
                _map[baseAddress] = cluster;
            }

            return cluster;
        }
    }
}
