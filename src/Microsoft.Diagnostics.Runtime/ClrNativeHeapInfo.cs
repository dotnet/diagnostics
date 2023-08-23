// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A region of native memory allocated by CLR.
    /// </summary>
    public class ClrNativeHeapInfo
    {
        /// <summary>
        /// The range of memory of this heap.
        /// </summary>
        public MemoryRange MemoryRange { get; }

        /// <summary>
        /// The kind of heap this is.
        /// </summary>
        public NativeHeapKind Kind { get; }

        /// <summary>
        /// The additional state info of this memory, if applicable.
        /// </summary>
        public ClrNativeHeapState State { get; }

        /// <summary>
        /// The ClrSubHeap index associated with this native heap or -1 if none.
        /// </summary>
        public int GCHeap { get; } = -1;

        /// <summary>
        /// Constructor.
        /// </summary>
        public ClrNativeHeapInfo(MemoryRange memory, NativeHeapKind kind, ClrNativeHeapState state)
        {
            MemoryRange = memory;
            Kind = kind;
            State = state;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public ClrNativeHeapInfo(MemoryRange memory, NativeHeapKind kind, ClrNativeHeapState state, int heap)
            :this(memory, kind, state)
        {
            GCHeap = heap;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (MemoryRange.Length > 0)
                return $"[{MemoryRange.Start:x},{MemoryRange.End:x}] - {Kind}";

            return $"{MemoryRange.Start:x} - {Kind}";
        }
    }
}