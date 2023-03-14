﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal sealed class HeapWithFilters
    {
        private int? _gcheap;
        private readonly ClrHeap _heap;

        /// <summary>
        /// Whether the heap will be filtered at all
        /// </summary>
        public bool HasFilters => _gcheap is not null || Segment is not null || MemoryRange is not null || MinimumObjectSize > 0 || MaximumObjectSize > 0;

        /// <summary>
        /// Only enumerate objects or segments within this range.
        /// </summary>
        public MemoryRange? MemoryRange { get; set; }

        /// <summary>
        /// Only enumerate the segment or objects on the segment which matches the given address.
        /// This address may be anywhere within a Segment's committed memory.
        /// </summary>
        public ulong? Segment { get; set; }

        /// <summary>
        /// The GC Heap number to filter on.
        /// </summary>
        public int? GCHeap
        {
            get => _gcheap;
            set
            {
                if (!_heap.SubHeaps.Any(sh => sh.Index == value))
                {
                    throw new ArgumentException($"No GC heap with index of {value}");
                }

                _gcheap = value;
            }
        }

        /// <summary>
        /// Whether or not to throw if there are now matching segments or subheaps.
        /// </summary>
        public bool ThrowIfNoMatchingGCRegions { get; set; } = true;

        /// <summary>
        /// The minimum size of an object to enumerate.
        /// </summary>
        public ulong MinimumObjectSize { get; set; }

        /// <summary>
        /// The maximum size of an object to enumerate
        /// </summary>
        public ulong MaximumObjectSize { get; set; }

        /// <summary>
        /// The order in which to enumerate segments.  This also applies to object enumeration.
        /// </summary>
        public Func<IEnumerable<ClrSegment>, IOrderedEnumerable<ClrSegment>> SortSegments { get; set; }

        /// <summary>
        /// The order in which to enumerate subheaps.  This only applies to subheap enumeration.
        /// </summary>
        public Func<IEnumerable<ClrSubHeap>, IOrderedEnumerable<ClrSubHeap>> SortSubHeaps { get; set; }

        public HeapWithFilters(ClrHeap heap)
        {
            _heap = heap;
            SortSegments = (seg) => seg.OrderBy(s => s.SubHeap.Index).ThenBy(s => s.Address);
            SortSubHeaps = (heap) => heap.OrderBy(heap => heap.Index);
        }

        public void FilterBySegmentHex(string segmentStr)
        {
            if (!ulong.TryParse(segmentStr, NumberStyles.HexNumber, null, out ulong segment))
            {
                throw new ArgumentException($"Invalid segment address: {segmentStr}");
            }

            if (ThrowIfNoMatchingGCRegions && !_heap.Segments.Any(seg => seg.Address == segment || seg.CommittedMemory.Contains(segment)))
            {
                throw new ArgumentException($"No segments match address: {segment:x}");
            }

            Segment = segment;
        }

        public void FilterByStringMemoryRange(string[] memoryRange, string commandName)
        {
            if (memoryRange.Length > 0)
            {
                if (memoryRange.Length > 2)
                {
                    string badArgument = memoryRange.FirstOrDefault(f => f.StartsWith("-") || f.StartsWith("/"));
                    if (badArgument != null)
                    {
                        throw new ArgumentException($"Unknown argument: {badArgument}");
                    }

                    throw new ArgumentException($"Too many arguments to !{commandName}");
                }

                string start = memoryRange[0];
                string end = memoryRange.Length > 1 ? memoryRange[1] : null;
                FilterByHexMemoryRange(start, end);
            }
        }

        public void FilterByHexMemoryRange(string startStr, string endStr)
        {
            if (!ulong.TryParse(startStr, NumberStyles.HexNumber, null, out ulong start))
            {
                throw new ArgumentException($"Invalid start address: {startStr}");
            }

            if (string.IsNullOrWhiteSpace(endStr))
            {
                MemoryRange = new(start, ulong.MaxValue);
            }
            else
            {
                bool length = false;
                if (endStr.StartsWith("L"))
                {
                    length = true;
                    endStr = endStr.Substring(1);
                }

                if (!ulong.TryParse(endStr, NumberStyles.HexNumber, null, out ulong end))
                {
                    throw new ArgumentException($"Invalid end address: {endStr}");
                }

                if (length)
                {
                    end += start;
                }

                if (end <= start)
                {
                    throw new ArgumentException($"Start address must be before end address: '{startStr}' < '{endStr}'");
                }

                MemoryRange = new(start, end);
            }

            if (ThrowIfNoMatchingGCRegions && !_heap.Segments.Any(seg => seg.CommittedMemory.Overlaps(MemoryRange.Value)))
            {
                throw new ArgumentException($"No segments or objects in range {MemoryRange.Value}");
            }
        }

        public IEnumerable<ClrSubHeap> EnumerateFilteredSubHeaps()
        {
            IEnumerable<ClrSubHeap> subheaps = _heap.SubHeaps;
            if (GCHeap is int gcheap)
            {
                subheaps = subheaps.Where(heap => heap.Index == gcheap);
            }

            if (Segment is ulong segment)
            {
                subheaps = subheaps.Where(heap => heap.Segments.Any(seg => seg.Address == segment || seg.CommittedMemory.Contains(segment)));
            }

            if (MemoryRange is MemoryRange range)
            {
                subheaps = subheaps.Where(heap => heap.Segments.Any(seg => seg.CommittedMemory.Overlaps(range)));
            }

            if (SortSubHeaps is not null)
            {
                subheaps = SortSubHeaps(subheaps);
            }

            return subheaps;
        }

        public IEnumerable<ClrSegment> EnumerateFilteredSegments() => EnumerateFilteredSegments(null);

        public IEnumerable<ClrSegment> EnumerateFilteredSegments(ClrSubHeap subheap)
        {
            IEnumerable<ClrSegment> segments = subheap != null ? subheap.Segments : _heap.Segments;
            if (GCHeap is int gcheap)
            {
                segments = segments.Where(seg => seg.SubHeap.Index == gcheap);
            }

            if (Segment is ulong segment)
            {
                segments = segments.Where(seg => seg.Address ==  segment || seg.CommittedMemory.Contains(segment));
            }

            if (MemoryRange is MemoryRange range)
            {
                segments = segments.Where(seg => seg.CommittedMemory.Overlaps(range));
            }

            if (SortSegments is not null)
            {
                segments = SortSegments(segments);
            }

            return segments;
        }

        public IEnumerable<ClrObject> EnumerateFilteredObjects(CancellationToken cancellation)
        {
            foreach (ClrSegment segment in EnumerateFilteredSegments())
            {
                IEnumerable<ClrObject> objs;
                if (MemoryRange is MemoryRange range)
                {
                    objs = segment.EnumerateObjects(range, carefully: true);
                }
                else
                {
                    objs = segment.EnumerateObjects(carefully: true);
                }

                foreach (ClrObject obj in objs)
                {
                    cancellation.ThrowIfCancellationRequested();

                    if (obj.IsValid)
                    {
                        ulong size = obj.Size;
                        if (MinimumObjectSize != 0 && size < MinimumObjectSize)
                        {
                            continue;
                        }

                        if (MaximumObjectSize != 0 && size > MaximumObjectSize)
                        {
                            continue;
                        }
                    }

                    yield return obj;
                }
            }
        }
    }
}
