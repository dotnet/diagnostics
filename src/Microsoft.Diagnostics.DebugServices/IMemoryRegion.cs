// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    public enum MemoryRegionType
    {
        MEM_UNKNOWN = 0,
        MEM_IMAGE = 0x1000000,
        MEM_MAPPED = 0x40000,
        MEM_PRIVATE = 0x20000
    }

    public enum MemoryRegionState
    {
        MEM_UNKNOWN = 0,
        MEM_COMMIT = 0x1000,
        MEM_FREE = 0x10000,
        MEM_RESERVE = 0x2000
    }

    public enum MemoryRegionProtection
    {
        PAGE_UNKNOWN = 0,
        PAGE_EXECUTE = 0x00000010,
        PAGE_EXECUTE_READ = 0x00000020,
        PAGE_EXECUTE_READWRITE = 0x00000040,
        PAGE_EXECUTE_WRITECOPY = 0x00000080,
        PAGE_NOACCESS = 0x00000001,
        PAGE_READONLY = 0x00000002,
        PAGE_READWRITE = 0x00000004,
        PAGE_WRITECOPY = 0x00000008,
        PAGE_GUARD = 0x00000100,
        PAGE_NOCACHE = 0x00000200,
        PAGE_WRITECOMBINE = 0x00000400
    }

    public enum MemoryRegionUsage
    {
        Unknown,
        Free,
        Image,
        Peb,
        Teb,
        Stack,
        Heap,
        PageHeap,
        FileMapping,
        CLR,
        Other,
    }

    /// <summary>
    /// Represents a single virtual address region in the target process.
    /// </summary>
    public interface IMemoryRegion
    {
        /// <summary>
        /// The start address of the region.
        /// </summary>
        ulong Start { get; }

        /// <summary>
        /// The end address of the region.
        /// </summary>
        ulong End { get; }

        /// <summary>
        /// The size of the region.
        /// </summary>
        ulong Size { get; }

        /// <summary>
        /// The type of the region. (Image/Private/Mapped)
        /// </summary>
        MemoryRegionType Type { get; }

        /// <summary>
        /// The state of the region. (Commit/Free/Reserve)
        /// </summary>
        MemoryRegionState State { get; }

        /// <summary>
        /// The protection of the region.
        /// </summary>
        MemoryRegionProtection Protection { get; }

        /// <summary>
        /// What this memory is being used for.
        /// This field is a best attempt at determining what the memory is being used for,
        /// and may be marked as Unknown if certain debugging symbols are not available.
        /// </summary>
        MemoryRegionUsage Usage { get; }

        /// <summary>
        /// If this file is an image or mapped file, this property may be non-null and
        /// contain its path.
        /// </summary>
        public string Image { get; }
    }
}
