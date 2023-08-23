// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents a range of memory in the target process.
    /// </summary>
    public readonly struct MemoryRange
    {
        /// <summary>
        /// Creates a memory range from an address and its length.
        /// </summary>
        /// <param name="start">The start address.</param>
        /// <param name="length">The length of the range.</param>
        /// <returns></returns>
        public static MemoryRange CreateFromLength(ulong start, ulong length) => new(start, start + length);

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="start">The start of the memory range.</param>
        /// <param name="end">The end of the memory range.</param>
        public MemoryRange(ulong start, ulong end)
        {
            Start = start;
            End = end;

            DebugOnly.Assert(start <= end);
        }

        /// <summary>
        /// The inclusive start address of the memory range.
        /// </summary>
        public ulong Start { get; }

        /// <summary>
        /// The exclusive end address of the memory range.
        /// </summary>
        public ulong End { get; }

        /// <summary>
        /// The length of the memory range in bytes.
        /// </summary>
        public ulong Length
        {
            get
            {
                if (End < Start)
                    return 0;

                return End - Start;
            }
        }

        /// <summary>
        /// Returns whether the memory range contains the given address.
        /// </summary>
        /// <param name="address">The address to check.</param>
        /// <returns>True if the memory range contains the given address.</returns>
        public bool Contains(ulong address) => Start <= address && address < Start + Length;

        /// <summary>
        /// Returns whether this memory range and <paramref name="other"/> contains any addresses which
        /// overlap.
        /// </summary>
        /// <param name="other">The other memory range to compare this to.</param>
        /// <returns>True if memory ranges overlap at all.</returns>
        public bool Overlaps(MemoryRange other) => other.Length > 0 && (Contains(other.Start) || Contains(other.End - 1) || other.Contains(Start) || other.Contains(End - 1));

        /// <summary>
        /// Returns whether this memory range contains all of <paramref name="other"/>.
        /// </summary>
        /// <param name="other">The other memory range to compare this to.</param>
        /// <returns>True if this memory range completely encloses <paramref name="other"/>.</returns>
        public bool Contains(MemoryRange other) => other.Length > 0 && (Contains(other.Start) && Contains(other.End - 1));

        /// <summary>
        /// Returns the range of memory in interval form, ie [start,end).  Since End is not inclusive, we use ')' to denote
        /// that the range does not include that address.
        /// </summary>
        public override string ToString() => $"[{Start:x},{End:x})";

        /// <summary>
        /// CompareTo implementation for a single address.
        /// </summary>
        public int CompareTo(ulong address)
        {
            if (address < Start)
                return 1;

            if (address >= End)
                return -1;

            return 0;
        }
    }
}