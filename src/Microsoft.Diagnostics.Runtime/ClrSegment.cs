// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A ClrSegment represents a contiguous region of memory that is devoted to the GC heap.
    /// Segments.  It has a start and end and knows what heap it belongs to.   Segments can
    /// optional have regions for Gen 0, 1 and 2, and Large properties.
    /// </summary>
    public sealed class ClrSegment : IClrSegment, IEquatable<ClrSegment>
    {
        private uint[]? _markers;

        internal ClrSegment(ClrSubHeap subHeap)
        {
            SubHeap = subHeap;
        }

        public bool Equals(IClrSegment? other)
        {
            return other is not null && other.Start == Start && other.End == End;
        }

        public bool Equals(ClrSegment? other)
        {
            return other is not null && other.Start == Start && other.End == End;
        }

        public override bool Equals(object? obj)
        {
            if (obj is IClrSegment other)
                return other.Equals(this);

            return false;
        }

        public override int GetHashCode() => Start.GetHashCode() ^ End.GetHashCode();

        /// <summary>
        /// The address of the CLR segment object.
        /// </summary>
        public ulong Address { get; internal set; }

        /// <summary>
        /// The memory range of the segment on which objects are allocated.  All objects in this segment fall within this range.
        /// </summary>
        public MemoryRange ObjectRange { get; internal set; }

        /// <summary>
        /// Gets the start address of the segment.  Equivalent to <see cref="ObjectRange"/>.<see cref="Start"/>.
        /// </summary>
        public ulong Start => ObjectRange.Start;

        /// <summary>
        /// Gets the end address of the segment.  Equivalent to <see cref="ObjectRange"/>.<see cref="Length"/>.
        /// </summary>
        public ulong End => ObjectRange.End;

        /// <summary>
        /// Equivalent to <see cref="ObjectRange"/>.<see cref="Length"/>.
        /// </summary>
        public ulong Length => ObjectRange.Length;

        /// <summary>
        /// Gets the SubHeap this segment lives on.
        /// </summary>
        public ClrSubHeap SubHeap { get; }

        public ClrSegmentFlags Flags { get; internal set; }

        /// <summary>
        /// Gets the range of memory reserved (but not committed) for this segment.
        /// </summary>
        public MemoryRange ReservedMemory { get; internal set; }

        /// <summary>
        /// Gets the range of memory committed for the segment (this may be larger than MemoryRange).
        /// </summary>
        public MemoryRange CommittedMemory { get; internal set; }

        /// <summary>
        /// Gets the first object on this segment or 0 if this segment contains no objects.
        /// </summary>
        public ulong FirstObjectAddress => ObjectRange.Start;

        /// <summary>
        /// The kind of segment this is.
        /// </summary>
        public GCSegmentKind Kind { get; internal set; }

        /// <summary>
        /// Returns true if the objects in this segment are pinned and cannot be relocated.
        /// </summary>
        public bool IsPinned => Kind is GCSegmentKind.Pinned or GCSegmentKind.Large or GCSegmentKind.Frozen;

        /// <summary>
        /// The memory range for Generation 0 on this segment.  This will be empty if this is not an ephemeral segment.
        /// </summary>
        public MemoryRange Generation0 { get; internal set; }

        /// <summary>
        /// The memory range for Generation 1 on this segment.  This will be empty if this is not an ephemeral segment.
        /// </summary>
        public MemoryRange Generation1 { get; internal set; }

        /// <summary>
        /// The memory range for Generation 2 on this segment.  This will be empty if this is not an ephemeral segment.
        /// </summary>
        public MemoryRange Generation2 { get; internal set; }

        /// <summary>
        /// Enumerates all objects on the segment.
        /// </summary>
        public IEnumerable<ClrObject> EnumerateObjects(bool carefully = false) => SubHeap.Heap.EnumerateObjects(this, carefully);

        /// <summary>
        /// Enumerates objects on the segment within the given memory range.
        /// </summary>
        public IEnumerable<ClrObject> EnumerateObjects(MemoryRange range, bool carefully = false)
        {
            if (ObjectRange.Overlaps(range))
            {
                ulong start = Math.Max(range.Start, ObjectRange.Start);

                foreach (ClrObject obj in SubHeap.Heap.EnumerateObjects(this, start, carefully))
                {
                    if (obj < range.Start)
                        continue;

                    if (obj < range.End)
                        yield return obj;
                    else
                        break;
                }
            }
        }

        internal ulong MaxObjectSize => Kind switch
        {
            GCSegmentKind.Frozen => int.MaxValue,
            GCSegmentKind.Pinned => int.MaxValue,
            GCSegmentKind.Large => int.MaxValue,
            _ => 85000
        };

        /// <summary>
        /// Returns the generation of an object in this segment.
        /// </summary>
        /// <param name="obj">An object in this segment.</param>
        /// <returns>
        /// The generation of the given object if that object lies in this segment.
        /// </returns>
        public Generation GetGeneration(ulong obj)
        {
            if (Kind <= GCSegmentKind.Frozen)
                return (Generation)Kind;

            if (Kind == GCSegmentKind.Ephemeral)
            {
                if (Generation0.Contains(obj))
                    return Generation.Generation0;

                if (Generation1.Contains(obj))
                    return Generation.Generation1;

                if (Generation2.Contains(obj))
                    return Generation.Generation2;
            }

            return Generation.Unknown;
        }

        /// <summary>
        /// Returns a string representation of this object.
        /// </summary>
        /// <returns>A string representation of this object.</returns>
        public override string ToString()
        {
            return $"{Kind} [{Start:x12}, {End:x12}]";
        }

        IEnumerable<IClrValue> IClrSegment.EnumerateObjects(bool carefully) => EnumerateObjects(carefully).Cast<IClrValue>();
        IEnumerable<IClrValue> IClrSegment.EnumerateObjects(MemoryRange range, bool carefully) => EnumerateObjects(range, carefully).Cast<IClrValue>();

        internal uint[] ObjectMarkers
        {
            get
            {
                uint[]? markers = _markers;
                if (markers is not null)
                    return markers;

                int len = ObjectRange.Length switch
                {
                    < 8 * 1024 => 0,
                    < 64 * 1024 * 1024 => 64,
                    < 256 * 1024 * 1024 => 128,
                    _ => 256,
                };

                markers = new uint[len];
                _markers = markers;
                return markers;
            }
        }

        /// <summary>
        /// The next segment in the heap.
        /// </summary>
        internal ulong Next { get; set; }

        IClrSubHeap IClrSegment.SubHeap => SubHeap;
        internal ulong BackgroundAllocated { get; set; }
    }
}