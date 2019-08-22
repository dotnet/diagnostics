// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Allocates and frees memory in the target process. Currently this is only available on Windows debuggers (dbgeng).
    /// </summary>
    public interface IRemoteMemoryService
    {
        /// <summary>
        /// Allocate memory in the target process. Similar to VirtualAllocEx on Windows.
        /// </summary>
        /// <param name="address">desired starting address</param>
        /// <param name="size">size of memory region in bytes</param>
        /// <param name="typeFlags">Type of memory allocation. See the MEM_* constants.</param>
        /// <param name="protectFlags">memory protection flags for the pages to be allocated. See the PAGE_* constants.</param>
        /// <param name="remoteAddress"></param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        bool AllocateMemory(ulong address, uint size, uint typeFlags, uint protectFlags, out ulong remoteAddress);

        /// <summary>
        /// Write memory into target process for supported targets. Similar to VirtualFreeEx on Windows.
        /// </summary>
        /// <param name="address">The address of memory to write</param>
        /// <param name="size">size of memory region in bytes</param>
        /// <param name="typeFlags"></param>
        /// <returns>true if any bytes where written, false if write failed</returns>
        bool FreeMemory(ulong address, uint size, uint typeFlags);
    }
}
