// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal sealed class CachedMemoryReader : MinidumpMemoryReader
    {
        private readonly ImmutableArray<MinidumpSegment> _segments;
        private readonly bool _leaveOpen;
        private readonly HeapSegmentDataCache _cachedMemorySegments;

        // Single element MinidumpSegment cache for last accessed segment to avoid needing to look it up before reading from its cache entry. The idea is most sequence of reads
        // have either strong locality (i.e. contiguous) or weak locality (if not contiguous at least within the same heap segment), so for all that meet that criteria avoid the
        // (somewhat expensive) binary search to go from address -> MinidumpSegment, and go directly to the data.
        //
        // NOTE: This technically leaks since we can't really clean up TLS entries, that said the MinidumpSegment only holds on to two ulongs, and then the int in the unnamed tuple, so this leaks
        // 20 bytes per thread that makes reads into the cache. Do not add anything more expensive to this cache or hold onto anything heavyweight like the actual cache entry that backs
        // the given heap memory.
        [ThreadStatic]
        private static (MinidumpSegment Segment, int SegmentIndex) _lastAccessed;

        private readonly object _rvaLock = new();
        private Stream? _rvaStream;

        public string DumpPath { get; }
        public override int PointerSize { get; }
        public CacheTechnology CacheTechnology { get; }
        public long MaxCacheSize { get; }

        public const int MinimumCacheSize = 0x200_0000;

        public CachedMemoryReader(ImmutableArray<MinidumpSegment> segments, string dumpPath, FileStream stream, long maxCacheSize, CacheTechnology cacheTechnology, int pointerSize, bool leaveOpen)
        {
            PointerSize = pointerSize;
            DumpPath = dumpPath;
            MaxCacheSize = maxCacheSize;
            CacheTechnology = cacheTechnology;
            _segments = segments;
            _leaveOpen = leaveOpen;

            if (CacheTechnology == CacheTechnology.AWE)
            {
                CacheNativeMethods.Util.SYSTEM_INFO sysInfo = default;
                CacheNativeMethods.Util.GetSystemInfo(ref sysInfo);

                // The AWE cache allocates on VirtualAlloc sized pages, which are 64k, if the majority of heap segments in the dump are < 64k this can be wasteful
                // of memory (in the extreme we can end using 64k of VM to store < 100 bytes), in this case we will force the cache technology to be the array pool.
                int segmentsBelow64K = _segments.Sum((hs) => hs.Size < sysInfo.dwAllocationGranularity ? 1 : 0);
                if (segmentsBelow64K > (int)(_segments.Length * 0.80))
                    CacheTechnology = CacheTechnology.ArrayPool;
            }

            if ((CacheTechnology == CacheTechnology.AWE) &&
                CacheNativeMethods.Util.EnableDisablePrivilege("SeLockMemoryPrivilege", enable: true))
            {
                _rvaStream = stream;

                // If we have the ability to lock physical memory in memory and the user has requested we use AWE, then do so, for best performance
                uint largestSegment = _segments.Max((hs) => (uint)hs.Size);

                // Create a a single large page, the size of the largest heap segment, we will read each in turn into this one large segment before
                // splitting them into (potentially) multiple VirtualAlloc pages.
                AWEBasedCacheEntryFactory cacheEntryFactory = new(stream.SafeFileHandle.DangerousGetHandle());
                cacheEntryFactory.CreateSharedSegment(largestSegment);

                _cachedMemorySegments = new HeapSegmentDataCache(cacheEntryFactory, entryCountWhenFull: (uint)_segments.Length, cacheIsFullyPopulatedBeforeUse: true, MaxCacheSize);

                // Force the cache entry creation, this is because the AWE factory will read the heap segment data from the file into physical memory, it is FAR
                // better for perf if we read it all in one continuous go instead of piece-meal as needed and it allows us to elide locks on the first level of the cache.
                foreach (MinidumpSegment segment in _segments)
                    _cachedMemorySegments.CreateAndAddEntry(segment);

                // We are done using the shared segment so we can release it now
                cacheEntryFactory.DeleteSharedSegment();
            }
            else
            {
                // We can't add the lock memory privilege, so just fall back on our ArrayPool/MemoryMappedFile based cache
                _cachedMemorySegments = new HeapSegmentDataCache(new ArrayPoolBasedCacheEntryFactory(stream, leaveOpen), entryCountWhenFull: (uint)_segments.Length, cacheIsFullyPopulatedBeforeUse: true, MaxCacheSize);

                // Force creation of empty entries for each segment, this won't map the data in from disk but it WILL prevent us from needing to take any locks at the first level of the cache
                // (the individual entries, when asked for data requiring page-in or when evicting data in page-out will still take locks for consistency).
                foreach (MinidumpSegment segment in _segments)
                    _cachedMemorySegments.CreateAndAddEntry(segment);
            }
        }

        public override int ReadFromRva(ulong rva, Span<byte> buffer)
        {
            lock (_rvaLock)
            {
                _rvaStream ??= File.OpenRead(DumpPath);
                _rvaStream.Position = (long)rva;
                return _rvaStream.Read(buffer);
            }
        }

        public override unsafe int Read(ulong address, Span<byte> buffer)
        {
            fixed (void* pBuffer = buffer)
                return TryReadMemory(address, buffer.Length, new IntPtr(pBuffer));
        }

        internal int TryReadMemory(ulong address, int byteCount, IntPtr buffer)
        {
            ImmutableArray<MinidumpSegment> segments = _segments;
            MinidumpSegment lastKnownSegment = segments[segments.Length - 1];

            // quick check if the address is before our first segment or after our last
            if ((address < segments[0].VirtualAddress) || (address > (lastKnownSegment.VirtualAddress + lastKnownSegment.Size)))
                return 0;

            int curSegmentIndex = -1;
            MinidumpSegment targetSegment;

            if (address < _lastAccessed.Segment.End && address >= _lastAccessed.Segment.VirtualAddress)
            {
                targetSegment = _lastAccessed.Segment;
                curSegmentIndex = _lastAccessed.SegmentIndex;
            }
            else
            {
                int memorySegmentStartIndex = segments.Search(address, (x, addr) => (x.VirtualAddress <= addr && addr < x.VirtualAddress + x.Size) ? 0 : x.VirtualAddress.CompareTo(addr));

                if (memorySegmentStartIndex >= 0)
                {
                    curSegmentIndex = memorySegmentStartIndex;
                }
                else
                {
                    // It would be beyond the end of the memory segments we have
                    if (memorySegmentStartIndex == ~segments.Length)
                        return 0;

                    // This is the index of the first segment of memory whose start address is GREATER than the given address.
                    int insertionIndex = ~memorySegmentStartIndex;
                    if (insertionIndex == 0)
                        return 0;

                    // Grab the segment before this one, as it must be the one that contains this address
                    curSegmentIndex = insertionIndex - 1;
                }

                targetSegment = segments[curSegmentIndex];
            }

            // This can only be true if we went into the else block above, located a segment BEYOND the given address, backed up one segment and the address
            // isn't inside that segment. This means we don't have the requested memory in the dump.
            if (address > targetSegment.End)
                return 0;

            IntPtr insertionPtr = buffer;
            int totalBytes = 0;

            int remainingBytes = byteCount;
            while (true)
            {
                ReadBytesFromSegment(targetSegment, address, remainingBytes, insertionPtr, out int bytesRead);

                totalBytes += bytesRead;
                remainingBytes -= bytesRead;

                if (remainingBytes == 0 || bytesRead == 0)
                {
                    _lastAccessed.Segment = targetSegment;
                    _lastAccessed.SegmentIndex = curSegmentIndex;

                    return totalBytes;
                }

                insertionPtr += bytesRead;
                address += (uint)bytesRead;

                if ((curSegmentIndex + 1) == segments.Length)
                {
                    _lastAccessed.Segment = targetSegment;
                    _lastAccessed.SegmentIndex = curSegmentIndex;

                    return totalBytes;
                }

                targetSegment = segments[++curSegmentIndex];

                if (address != targetSegment.VirtualAddress)
                {
                    int prevSegmentIndex = curSegmentIndex;

                    curSegmentIndex = segments.Search(address, (x, addr) => (x.VirtualAddress <= addr && addr < x.VirtualAddress + x.Size) ? 0 : x.VirtualAddress.CompareTo(addr));
                    if (curSegmentIndex == -1)
                    {
                        if (prevSegmentIndex >= 0)
                        {
                            _lastAccessed.SegmentIndex = prevSegmentIndex;
                            _lastAccessed.Segment = segments[_lastAccessed.SegmentIndex];
                        }

                        return totalBytes;
                    }

                    targetSegment = segments[curSegmentIndex];
                }
            }
        }

        private SegmentCacheEntry GetCacheEntryForMemorySegment(MinidumpSegment memorySegment)
        {
            // NOTE: We assume the caller has triggered cachedMemorySegments initialization in the fetching of the MemorySegmentData they have given us
            if (_cachedMemorySegments.TryGetCacheEntry(memorySegment.VirtualAddress, out SegmentCacheEntry? entry))
                return entry!;

            return _cachedMemorySegments.CreateAndAddEntry(memorySegment);
        }

        private void ReadBytesFromSegment(MinidumpSegment segment, ulong startAddress, int byteCount, IntPtr buffer, out int bytesRead)
        {
            SegmentCacheEntry cacheEntry = GetCacheEntryForMemorySegment(segment);
            cacheEntry.GetDataForAddress(startAddress, (uint)byteCount, buffer, out uint read);
            bytesRead = (int)read;
        }

        public override void Dispose()
        {
            _cachedMemorySegments.Dispose();
            if (!_leaveOpen)
                _rvaStream?.Dispose();
        }
    }
}