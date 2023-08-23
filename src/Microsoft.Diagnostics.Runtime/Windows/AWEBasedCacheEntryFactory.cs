// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal sealed class AWEBasedCacheEntryFactory : SegmentCacheEntryFactory
    {
        private readonly IntPtr _dumpFileHandle;
        private UIntPtr _sharedSegment;

        internal AWEBasedCacheEntryFactory(IntPtr dumpFileHandle)
        {
            _dumpFileHandle = dumpFileHandle;
        }

        internal void CreateSharedSegment(uint size)
        {
            if (_sharedSegment != UIntPtr.Zero)
                CacheNativeMethods.Memory.VirtualFree(_sharedSegment, sizeToFree: UIntPtr.Zero, CacheNativeMethods.Memory.VirtualFreeType.Release);

            // TODO: If I don't ensure that size is a multiple of the VA page size I leave chunks of unusable memory, then again allocating on a page boundary and not using it all is also
            // not using the memory, so not sure if it matters/helps to worry about it here.
            _sharedSegment = CacheNativeMethods.Memory.VirtualAlloc(size, CacheNativeMethods.Memory.VirtualAllocType.Reserve | CacheNativeMethods.Memory.VirtualAllocType.Physical, CacheNativeMethods.Memory.MemoryProtection.ReadWrite);
        }

        internal void DeleteSharedSegment()
        {
            if (_sharedSegment != UIntPtr.Zero)
                CacheNativeMethods.Memory.VirtualFree(_sharedSegment, sizeToFree: UIntPtr.Zero, CacheNativeMethods.Memory.VirtualFreeType.Release);

            _sharedSegment = UIntPtr.Zero;
        }

        public override SegmentCacheEntry CreateEntryForSegment(MinidumpSegment segmentData, Action<uint> updateOwningCacheForSizeChangeCallback)
        {
            bool setFPRes = CacheNativeMethods.File.SetFilePointerEx(_dumpFileHandle, (long)segmentData.FileOffset, SeekOrigin.Begin);
            if (!setFPRes)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            uint numberOfPages = (uint)(segmentData.Size / (ulong)Environment.SystemPageSize);
            if ((segmentData.Size % (ulong)Environment.SystemPageSize) != 0)
                numberOfPages++;

            uint bytesNeededForPageArray = numberOfPages * (uint)IntPtr.Size;
            UIntPtr pageFrameArray = CacheNativeMethods.Memory.HeapAlloc(bytesNeededForPageArray);
            uint numberOfPagesAllocated = numberOfPages;
            bool handedAllocationsToCacheEntry = false;

            // Allocate the physical memory for our pages and store the mapping data into our page frame array
            try
            {
                // Allocate the physical memory, this claims this much physical memory but it does not yet count against our process usage limits
                bool physicalAllocRes = CacheNativeMethods.AWE.AllocateUserPhysicalPages(ref numberOfPagesAllocated, pageFrameArray);
                if (!physicalAllocRes)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (numberOfPagesAllocated != numberOfPages)
                    throw new OutOfMemoryException("Failed to allocate the required number of pages for segment in AWE based cache.");

                UIntPtr reservedMemory = UIntPtr.Zero;
                try
                {
                    // Now reserve a chunk of VM equivalent in size to the physical memory, this will now count against our process usage limits, but only temporarily
                    reservedMemory = _sharedSegment != UIntPtr.Zero ? _sharedSegment : CacheNativeMethods.Memory.VirtualAlloc((uint)segmentData.Size,
                                                                                                                                      CacheNativeMethods.Memory.VirtualAllocType.Reserve | CacheNativeMethods.Memory.VirtualAllocType.Physical,
                                                                                                                                      CacheNativeMethods.Memory.MemoryProtection.ReadWrite);

                    // Now assign our previously reserved physical memory to the VM range we just reserved
                    bool mapPhysicalPagesResult = CacheNativeMethods.AWE.MapUserPhysicalPages(reservedMemory, numberOfPages, pageFrameArray);
                    if (!mapPhysicalPagesResult)
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                    // Now that the physical memory is mapped into our VM space, fill it with data from the heap segment from the dump
                    bool readFileRes = CacheNativeMethods.File.ReadFile(_dumpFileHandle, reservedMemory, (uint)segmentData.Size, out uint bytesRead);
                    if (!readFileRes)
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                    // Now for the magic, this line unmaps the physical memory from our process, it still counts against our process limits (until the VirtualFree below). Once we
                    // VirtualFree it below the memory 'cost' still is assigned to our process BUT it doesn't count against our resource limits until/unless we map it back into the VM space.
                    // BUT, most importantly, the physical memory remains and contains the data we read from the file.
                    bool unmapPhysicalPagesResult = CacheNativeMethods.AWE.MapUserPhysicalPages(reservedMemory, numberOfPages, UIntPtr.Zero);
                    if (!unmapPhysicalPagesResult)
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                    if (_sharedSegment != reservedMemory)
                    {
                        // Free the virtual memory we were using to map the physical memory. NOTE: sizeToFree must be 0 when we are calling with VirtualFreeType.Release
                        bool virtualFreeRes = CacheNativeMethods.Memory.VirtualFree(reservedMemory, sizeToFree: UIntPtr.Zero, CacheNativeMethods.Memory.VirtualFreeType.Release);
                        if (!virtualFreeRes)
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    reservedMemory = UIntPtr.Zero;

                    // Now give our page frame table to the AWE cache node so it can map the memory back into our VM as needed
                    handedAllocationsToCacheEntry = true;
                    return new AWEBasedCacheEntry(segmentData, updateOwningCacheForSizeChangeCallback, pageFrameArray, (int)numberOfPages);
                }
                finally
                {
                    // Something failed, clean up if we allocated memory
                    if (!handedAllocationsToCacheEntry && (reservedMemory != UIntPtr.Zero) && (_sharedSegment != reservedMemory))
                        CacheNativeMethods.Memory.VirtualFree(reservedMemory, UIntPtr.Zero, CacheNativeMethods.Memory.VirtualFreeType.Release);
                }
            }
            finally
            {
                // Something failed, clean up
                if (!handedAllocationsToCacheEntry)
                    CacheNativeMethods.AWE.FreeUserPhysicalPages(ref numberOfPagesAllocated, pageFrameArray);
            }
        }
    }
}
