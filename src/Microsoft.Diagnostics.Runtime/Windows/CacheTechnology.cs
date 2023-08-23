// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal enum CacheTechnology : byte
    {
        /// <summary>
        /// Indicates the cache should operate using the ArrayPool to managed byte[] of memory from the dump heap.
        /// </summary>
        ArrayPool = 1,

        /// <summary>
        /// Indicates the cache should operate using AWE (Address Windowing Extensions) to manage memory from the dump heap
        /// </summary>
        /// <remarks>NOTE: This option is ONLY possible if the user has the 'Lock Pages in Memory' permission, otherwise we will fall back on using <see cref="ArrayPool"/></remarks>
        AWE = 2
    }
}