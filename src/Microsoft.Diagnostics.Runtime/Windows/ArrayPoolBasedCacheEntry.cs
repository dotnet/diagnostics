// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Threading;

// TODO:  This code wasn't written to consider nullable.
#nullable disable

namespace Microsoft.Diagnostics.Runtime.Windows
{
    /// <summary>
    /// Represents heap segment cache entries backed by arrays from ArrayPool{byte}.Shared. This technology is less efficient than the <see cref="AWEBasedCacheEntry"/> but it has upsides
    /// around not requiring special privileges and mapping in a more granular fashion (4k pages vs 64k pages).
    /// </summary>

    internal sealed class ArrayPoolBasedCacheEntry : CacheEntryBase<byte[]>
    {
        private static readonly uint SystemPageSize = (uint)Environment.SystemPageSize;

        private readonly MemoryMappedFile _mappedFile;

        internal ArrayPoolBasedCacheEntry(MemoryMappedFile mappedFile, MinidumpSegment segmentData, Action<uint> updateOwningCacheForAddedChunk) : base(segmentData, derivedMinSize: 2 * IntPtr.Size, updateOwningCacheForAddedChunk)
        {
            _mappedFile = mappedFile;
        }

        public override long PageOutData()
        {
            ThrowIfDisposed();

            (ulong dataRemoved, uint _) = TryRemoveAllPagesFromCache(disposeLocks: false);

            int oldCurrent;
            int newCurrent;
            do
            {
                oldCurrent = _entrySize;
                newCurrent = Math.Max(MinSize, oldCurrent - (int)dataRemoved);
            }
            while (Interlocked.CompareExchange(ref _entrySize, newCurrent, oldCurrent) != oldCurrent);

            return (long)dataRemoved;
        }

        protected override uint EntryPageSize => SystemPageSize;

        protected override (byte[] Data, ulong DataExtent) GetPageDataAtOffset(ulong pageAlignedOffset)
        {
            // NOTE: The caller ensures this method is not called concurrently

            if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                HeapSegmentCacheEventSource.Instance.PageInDataStart((long)(_segmentData.VirtualAddress + pageAlignedOffset), EntryPageSize);

            uint readSize;
            if (pageAlignedOffset + EntryPageSize <= _segmentData.Size)
            {
                readSize = EntryPageSize;
            }
            else
            {
                readSize = (uint)(_segmentData.Size - pageAlignedOffset);
            }

            bool pageInFailed = false;
            using MemoryMappedViewAccessor view = _mappedFile.CreateViewAccessor((long)_segmentData.FileOffset + (long)pageAlignedOffset, size: readSize, MemoryMappedFileAccess.Read);
            try
            {
                ulong viewOffset = (ulong)view.PointerOffset;

                unsafe
                {
                    byte* pViewLoc = null;
                    try
                    {
                        view.SafeMemoryMappedViewHandle.AcquirePointer(ref pViewLoc);
                        if (pViewLoc == null)
                            throw new InvalidOperationException("Failed to acquire the underlying memory mapped view pointer. This is unexpected");

                        pViewLoc += viewOffset;

                        // Grab a shared buffer to use if there is one, or create one for the pool
                        byte[] data = ArrayPool<byte>.Shared.Rent((int)readSize);

                        // NOTE: This looks sightly ridiculous but view.ReadArray<T> is TERRIBLE for primitive types like byte, it calls Marshal.PtrToStructure for EVERY item in the
                        // array, the overhead of that call SWAMPS all access costs to the memory, and it is called N times (where N here is 4k), whereas memcpy just blasts the bits
                        // from one location to the other, it is literally a couple of orders of magnitude faster.
                        fixed (byte* pData = data)
                        {
                            CacheNativeMethods.Memory.memcpy(new UIntPtr(pData), new UIntPtr(pViewLoc), new UIntPtr(readSize));
                        }

                        return (data, readSize);
                    }
                    finally
                    {
                        if (pViewLoc != null)
                            view.SafeMemoryMappedViewHandle.ReleasePointer();
                    }
                }
            }
            catch (Exception ex)
            {
                if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                    HeapSegmentCacheEventSource.Instance.PageInDataFailed(ex.Message);

                pageInFailed = true;
                throw;
            }
            finally
            {
                if (!pageInFailed && HeapSegmentCacheEventSource.Instance.IsEnabled())
                    HeapSegmentCacheEventSource.Instance.PageInDataEnd((int)readSize);
            }
        }

        protected override uint InvokeCallbackWithDataPtr(CachePage<byte[]> page, Func<UIntPtr, ulong, uint> callback)
        {
            unsafe
            {
                fixed (byte* pBuffer = page.Data)
                {
                    return callback(new UIntPtr(pBuffer), page.DataExtent);
                }
            }
        }

        protected override uint CopyDataFromPage(CachePage<byte[]> page, IntPtr buffer, ulong inPageOffset, uint byteCount)
        {
            // Calculate how much of the requested read can be satisfied by the page
            uint sizeRead = (uint)Math.Min(page.DataExtent - inPageOffset, byteCount);

            unsafe
            {
                fixed (byte* pData = page.Data)
                {
                    CacheNativeMethods.Memory.memcpy(buffer, new UIntPtr(pData + inPageOffset), new UIntPtr(sizeRead));
                }
            }

            return sizeRead;
        }

        protected override void Dispose(bool disposing)
        {
            for (int i = 0; i < 3; i++)
            {
                if (TryRemoveAllPagesFromCache(disposeLocks: true).ItemsSkipped == 0)
                {
                    break;
                }
            }
        }

        private (ulong DataRemoved, uint ItemsSkipped) TryRemoveAllPagesFromCache(bool disposeLocks)
        {
            // Assume we will be able to evict all non-null pages
            ulong dataRemoved = 0;
            uint itemsSkipped = 0;

            for (int i = 0; i < _pages.Length; i++)
            {
                CachePage<byte[]> page = _pages[i];
                if (page != null)
                {
                    ReaderWriterLockSlim dataChunkLock = _pageLocks[i];
                    if (!dataChunkLock.TryEnterWriteLock(timeout: TimeSpan.Zero))
                    {
                        // Someone holds a read or write lock on this page, skip it
                        itemsSkipped++;
                        continue;
                    }

                    try
                    {
                        // double check that no other thread already scavenged this entry
                        page = _pages[i];
                        if (page != null)
                        {
                            ArrayPool<byte>.Shared.Return(page.Data);
                            dataRemoved += page.DataExtent;
                            _pages[i] = null;
                        }
                    }
                    finally
                    {
                        dataChunkLock.ExitWriteLock();
                        if (disposeLocks)
                        {
                            dataChunkLock.Dispose();
                            _pageLocks[i] = null;
                        }
                    }
                }
            }

            return (dataRemoved, itemsSkipped);
        }
    }
}