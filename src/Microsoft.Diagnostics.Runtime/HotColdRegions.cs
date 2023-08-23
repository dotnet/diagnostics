// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Returns the addresses and sizes of the hot and cold regions of a method.
    /// </summary>
    public readonly struct HotColdRegions
    {
        /// <summary>
        /// Gets the start address of the method's hot region.
        /// </summary>
        public ulong HotStart { get; }

        /// <summary>
        /// Gets the size of the hot region.
        /// </summary>
        public uint HotSize { get; }

        /// <summary>
        /// Gets the start address of the method's cold region.
        /// </summary>
        public ulong ColdStart { get; }

        /// <summary>
        /// Gets the size of the cold region.
        /// </summary>
        public uint ColdSize { get; }

        public HotColdRegions(ulong hotStart, uint hotSize, ulong coldStart, uint coldSize)
        {
            HotStart = hotStart;
            HotSize = hotSize;
            ColdStart = coldStart;
            ColdSize = coldSize;
        }
    }
}
