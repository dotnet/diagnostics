// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    public sealed class CacheOptions
    {
        public bool CacheTypes { get; set; } = true;
        public bool CacheFields { get; set; } = true;
        public bool CacheMethods { get; set; } = true;

        public StringCaching CacheTypeNames { get; set; } = StringCaching.Cache;
        public StringCaching CacheFieldNames { get; set; } = StringCaching.Cache;
        public StringCaching CacheMethodNames { get; set; } = StringCaching.Cache;

        /// <summary>
        /// The maximum amount of memory (virtual address space) used by data readers to cache
        /// memory from the dumpfile.
        /// </summary>
        public long MaxDumpCacheSize { get; set; } = IntPtr.Size == 8 ? 0x1_0000_0000 : 0x800_0000;

        /// <summary>
        /// Whether or not to attempt to use special OS memory features such as AWE on
        /// Windows.
        /// </summary>
        public bool UseOSMemoryFeatures { get; set; }
    }
}
