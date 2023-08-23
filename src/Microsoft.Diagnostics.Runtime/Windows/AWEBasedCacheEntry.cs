// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// TODO:  This code wasn't written to consider nullable.
#nullable disable

namespace Microsoft.Diagnostics.Runtime.Windows
{
    /// <summary>
    /// Represents heap segment cache entries backed by AWE (Address Windowing Extensions). This technology allows us to read the entirety of the heap data out of the dump (which is very fast disk
    /// access wise) up front, keep it in physical memory, but only maps those physical memory pages into our VM space as needed, allowing us to control how much memory we use and making 'mapping in'
    /// very fast(some page table entry work in Windows instead of physically reading the data off of disk). The downside is it requires the user have special privileges as well as it maps data
    /// in 64k chunks(the VirtualAlloc allocation granularity).
    /// </summary>
    internal sealed class AWEBasedCacheEntry : CacheEntryBase<UIntPtr>
    {
        private static readonly uint VirtualAllocPageSize = InitVirtualAllocPageSize();
        private static readonly int SystemPageSize = Environment.SystemPageSize;

        private UIntPtr _pageFrameArray;
        private readonly int _pageFrameArrayItemCount;

        private static uint InitVirtualAllocPageSize()
        {
            CacheNativeMethods.Util.SYSTEM_INFO sysInfo = default;
            CacheNativeMethods.Util.GetSystemInfo(ref sysInfo);

            return sysInfo.dwAllocationGranularity;
        }

        internal AWEBasedCacheEntry(MinidumpSegment segmentData, Action<uint> updateOwningCacheForSizeChangeCallback, UIntPtr pageFrameArray, int pageFrameArrayItemCount) : base(segmentData, derivedMinSize: UIntPtr.Size + (pageFrameArrayItemCount * UIntPtr.Size) + sizeof(int), updateOwningCacheForSizeChangeCallback)
        {
            _pageFrameArray = pageFrameArray;
            _pageFrameArrayItemCount = pageFrameArrayItemCount;
        }

        protected override uint EntryPageSize => AWEBasedCacheEntry.VirtualAllocPageSize;

        public override long PageOutData()
        {
            ThrowIfDisposed();

            if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                HeapSegmentCacheEventSource.Instance.PageOutDataStart();

            long sizeRemoved = 0;

            int maxLoopCount = 5;
            int pass = 0;
            for (; pass < maxLoopCount; pass++)
            {
                // Assume we will be able to evict all non-null pages
                bool encounteredBusyPage = false;

                for (int i = 0; i < _pages.Length; i++)
                {
                    ReaderWriterLockSlim pageLock = _pageLocks[i];
                    if (!pageLock.TryEnterWriteLock(timeout: TimeSpan.Zero))
                    {
                        // Someone holds the writelock on this page, skip it and try to get it in another pass, this prevent us from blocking page out
                        // on someone currently reading a page, they will likely be done by our next pass

                        encounteredBusyPage = true;
                        continue;
                    }

                    try
                    {
                        CachePage<UIntPtr> page = _pages[i];
                        if (page != null)
                        {
                            uint pagesToUnMap = (uint)(page.DataExtent / (ulong)SystemPageSize) + (uint)((page.DataExtent % (ulong)SystemPageSize) != 0 ? 1 : 0);

                            // We need to unmap the physical memory from this VM range and then free the VM range
                            bool unmapPhysicalPagesResult = CacheNativeMethods.AWE.MapUserPhysicalPages(page.Data, pagesToUnMap, pageArray: UIntPtr.Zero);
                            if (!unmapPhysicalPagesResult)
                            {
                                Debug.Fail("MapUserPhysicalPage failed to unmap a physical page");

                                // this is an error but we don't want to remove the ptr entry since we apparently didn't unmap the physical memory
                                continue;
                            }

                            sizeRemoved += (long)page.DataExtent;

                            bool virtualFreeRes = CacheNativeMethods.Memory.VirtualFree(page.Data, sizeToFree: UIntPtr.Zero, CacheNativeMethods.Memory.VirtualFreeType.Release);
                            if (!virtualFreeRes)
                            {
                                Debug.Fail("MapUserPhysicalPage failed to unmap a physical page");

                                // this is an error but we already unmapped the physical memory so also throw away our VM pointer
                                _pages[i] = null;

                                continue;
                            }

                            // Done, throw away our VM pointer
                            _pages[i] = null;
                        }
                    }
                    finally
                    {
                        pageLock.ExitWriteLock();
                    }
                }

                // We are done if we didn't encounter any busy (locked) pages
                if (!encounteredBusyPage)
                    break;
            }

            // Correct our size based on how much data we could remove
            int oldCurrent;
            int newCurrent;
            do
            {
                oldCurrent = _entrySize;
                newCurrent = Math.Max(MinSize, oldCurrent - (int)sizeRemoved);
            } while (Interlocked.CompareExchange(ref _entrySize, newCurrent, oldCurrent) != oldCurrent);

            if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                HeapSegmentCacheEventSource.Instance.PageOutDataEnd(sizeRemoved);

            return sizeRemoved;
        }

        protected override unsafe uint InvokeCallbackWithDataPtr(CachePage<UIntPtr> page, Func<UIntPtr, ulong, uint> callback)
        {
            return callback(page.Data, page.DataExtent);
        }

        protected override uint CopyDataFromPage(CachePage<UIntPtr> page, IntPtr buffer, ulong inPageOffset, uint byteCount)
        {
            // Calculate how much of the requested read can be satisfied by the page
            uint sizeRead = (uint)Math.Min(page.DataExtent - inPageOffset, byteCount);

            unsafe
            {
                CacheNativeMethods.Memory.memcpy(buffer, new UIntPtr((byte*)page.Data + inPageOffset), new UIntPtr(sizeRead));
            }

            return sizeRead;
        }

        protected override (UIntPtr Data, ulong DataExtent) GetPageDataAtOffset(ulong pageAlignedOffset)
        {
            // NOTE: The caller ensures this method is not called concurrently

            ulong readSize;
            if (pageAlignedOffset + EntryPageSize <= _segmentData.Size)
            {
                readSize = EntryPageSize;
            }
            else
            {
                readSize = _segmentData.Size - pageAlignedOffset;
            }

            if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                HeapSegmentCacheEventSource.Instance.PageInDataStart((long)(_segmentData.VirtualAddress + pageAlignedOffset), (long)readSize);

            ulong startingMemoryPageNumber = (pageAlignedOffset / (ulong)AWEBasedCacheEntry.SystemPageSize);

            try
            {
                // Allocate a VM range to map the physical memory into.
                //
                // NOTE: VirtualAlloc ALWAYS rounds allocation requests to the VirtualAllocPageSize, which is 64k. If you ask it for less the allocation will succeed but it will have
                // reserved 64k of memory, making that memory unusable for anyone else. If you do this a lot (say across an entire dump heap) you easily fragment memory to the point
                // of seeing sporadic allocation failures due to not being able to find enough contiguous memory. VMMAP (from SysInternals) is good for showing this kind of
                // fragmentation, it marks the excess space as 'Unusable Space'
                UIntPtr vmPtr = CacheNativeMethods.Memory.VirtualAlloc(EntryPageSize, CacheNativeMethods.Memory.VirtualAllocType.Reserve | CacheNativeMethods.Memory.VirtualAllocType.Physical, CacheNativeMethods.Memory.MemoryProtection.ReadWrite);
                if (vmPtr == UIntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                ulong numberOfPages = readSize / (uint)AWEBasedCacheEntry.SystemPageSize + (((readSize % (ulong)AWEBasedCacheEntry.SystemPageSize) == 0) ? 0u : 1u);

                // Map one VirtualAlloc sized page of our physical memory into the VM space, we have to adjust the pageFrameArray pointer as MapUserPhysicalPages only takes a page count and a page frame array starting point
                bool mapPhysicalPagesResult = CacheNativeMethods.AWE.MapUserPhysicalPages(
                    vmPtr,
                    numberOfPages,
                    new UIntPtr((ulong)_pageFrameArray + (startingMemoryPageNumber * (ulong)UIntPtr.Size))
                );

                if (!mapPhysicalPagesResult)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                    HeapSegmentCacheEventSource.Instance.PageInDataEnd((int)readSize);

                return (vmPtr, readSize);
            }
            catch (Exception ex)
            {
                if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                    HeapSegmentCacheEventSource.Instance.PageInDataFailed(ex.Message);

                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            for (int i = 0; i < _pages.Length; i++)
            {
                ReaderWriterLockSlim pageLock = _pageLocks[i];
                pageLock.EnterWriteLock();

                try
                {
                    CachePage<UIntPtr> page = _pages[i];
                    if (page != null)
                    {
                        // NOTE: While VirtualAllocPageSize SHOULD be a multiple of SystemPageSize there is no guarantee I can find that says that is true always and everywhere
                        // so to be safe I make sure we don't leave any straggling pages behind if that is true.
                        uint numberOfPages = (uint)(page.DataExtent / (ulong)SystemPageSize) + ((page.DataExtent % (ulong)SystemPageSize) == 0 ? 0U : 1U);

                        // We need to unmap the physical memory from this VM range and then free the VM range
                        bool unmapPhysicalPagesResult = CacheNativeMethods.AWE.MapUserPhysicalPages(page.Data, numberOfPages, pageArray: UIntPtr.Zero);
                        if (!unmapPhysicalPagesResult)
                        {
                            Debug.Fail("MapUserPhysicalPage failed to unmap a physical page");

                            // this is an error but we don't want to remove the ptr entry since we apparently didn't unmap the physical memory
                            continue;
                        }

                        // NOTE: When calling with VirtualFreeTypeRelease sizeToFree must be 0 (which indicates the entire allocation)
                        bool virtualFreeRes = CacheNativeMethods.Memory.VirtualFree(page.Data, sizeToFree: UIntPtr.Zero, CacheNativeMethods.Memory.VirtualFreeType.Release);
                        if (!virtualFreeRes)
                        {
                            Debug.Fail("MapUserPhysicalPage failed to unmap a physical page");

                            // this is an error but we already unmapped the physical memory so also throw away our VM pointer
                            _pages[i] = null;

                            continue;
                        }

                        // Done, throw away our VM pointer
                        _pages[i] = null;
                    }
                }
                finally
                {
                    pageLock.ExitWriteLock();

                    if (_pages[i] == null)
                    {
                        pageLock.Dispose();
                    }
                }
            }

            uint numberOfPagesToFree = (uint)_pageFrameArrayItemCount;
            bool freeUserPhyiscalPagesRes = CacheNativeMethods.AWE.FreeUserPhysicalPages(ref numberOfPagesToFree, _pageFrameArray);
            if (!freeUserPhyiscalPagesRes)
            {
                Debug.Fail("Failed to free our physical pages");
            }

            if (numberOfPagesToFree != _pageFrameArrayItemCount)
            {
                Debug.Fail("Failed to free ALL of our physical pages");
            }

            // Free our page frame array
            CacheNativeMethods.Memory.HeapFree(_pageFrameArray);
            _pageFrameArray = UIntPtr.Zero;
        }
    }
}
