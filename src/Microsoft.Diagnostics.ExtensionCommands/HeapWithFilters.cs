using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal class HeapWithFilters
    {
        private int? _gcheap;
        private readonly ClrHeap _heap;

        public bool HasFilters => _gcheap is not null || Segment is not null || MemoryRange is not null;

        public MemoryRange? MemoryRange { get; set; }
        public ulong? Segment { get; set; }
        public int? GCHeap
        {
            get => _gcheap;
            set
            {
                if (!_heap.SubHeaps.Any(sh => sh.Index == value))
                    throw new ArgumentException($"No GC heap with index of {value}");

                _gcheap = value;
            }
        }

        public Func<IEnumerable<ClrSegment>, IOrderedEnumerable<ClrSegment>> SortSegments { get; set; }

        public HeapWithFilters(ClrHeap heap)
        {
            _heap = heap;
            SortSegments = (seg) => seg.OrderBy(s => s.SubHeap.Index).ThenBy(s => s.Address);
        }

        public void FilterBySegmentHex(string segmentStr)
        {
            if (!ulong.TryParse(segmentStr, NumberStyles.HexNumber, null, out ulong segment))
                throw new ArgumentException($"Invalid segment address: {segmentStr}");

            if (!_heap.Segments.Any(seg => seg.Address == segment || seg.CommittedMemory.Contains(segment)))
                throw new ArgumentException($"No segments match address: {segment:x}");

            Segment = segment;
        }

        public void FilterByHexMemoryRange(string startStr, string endStr)
        {
            if (!ulong.TryParse(startStr, NumberStyles.HexNumber, null, out ulong start))
                throw new ArgumentException($"Invalid start address: {startStr}");

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
                    throw new ArgumentException($"Invalid end address: {endStr}");

                if (length)
                    end += start;

                if (end <= start)
                    throw new ArgumentException($"Start address must be before end address: '{startStr}' < '{endStr}'");

                MemoryRange = new(start, end);
            }

            if (!_heap.Segments.Any(seg => seg.CommittedMemory.Overlaps(MemoryRange.Value)))
                throw new ArgumentException($"No segments or objects in range {MemoryRange.Value}");
        }

        public IEnumerable<ClrSegment> EnumerateFilteredSegments()
        {
            IEnumerable<ClrSegment> segments = _heap.Segments;
            if (GCHeap is int gcheap)
                segments = segments.Where(seg => seg.SubHeap.Index == gcheap);

            if (Segment is ulong segment)
                segments = segments.Where(seg => seg.Address ==  segment || seg.CommittedMemory.Contains(segment));

            if (MemoryRange is MemoryRange range)
                segments = segments.Where(seg => seg.CommittedMemory.Overlaps(range));

            if (SortSegments is not null)
                segments = SortSegments(segments);

            return segments;
        }

        public IEnumerable<ClrObject> EnumerateFilteredObjects(CancellationToken cancellation)
        {
            foreach (ClrSegment segment in EnumerateFilteredSegments())
            {
                IEnumerable<ClrObject> objs;
                if (MemoryRange is MemoryRange range)
                    objs = segment.EnumerateObjects(range, carefully: true);
                else
                    objs = segment.EnumerateObjects(carefully: true);

                foreach (ClrObject obj in objs)
                {
                    cancellation.ThrowIfCancellationRequested();
                    yield return obj;
                }
            }
        }
    }
}
