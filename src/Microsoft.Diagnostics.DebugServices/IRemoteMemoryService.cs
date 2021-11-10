// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        /// <returns>true if the allocation succeeded, false otherwise</returns>
        bool AllocateMemory(ulong address, uint size, uint typeFlags, uint protectFlags, out ulong remoteAddress);

        /// <summary>
        /// Free memory the target process for supported targets. Similar to VirtualFreeEx on Windows.
        /// </summary>
        /// <param name="address">The address of memory to free</param>
        /// <param name="size">size of memory region in bytes</param>
        /// <param name="typeFlags">Type of memory allocation. See the MEM_* constants.</param>
        /// <returns>true if the requested memory range was properly freed, false otherwise</returns>
        bool FreeMemory(ulong address, uint size, uint typeFlags);
    }
}
