// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;

// TODO:  This code wasn't written to consider nullable.
#nullable disable

namespace Microsoft.Diagnostics.Runtime.Windows
{
    /// <summary>
    /// This class acts as the base of the two (ArrayPool and AWE) cache entry types.
    /// </summary>
    /// <typeparam name="T">The type of data the cache pages hold</typeparam>
    internal abstract class CacheEntryBase<T> : SegmentCacheEntry, IDisposable
    {
        protected CachePage<T>[] _pages;
        protected ReaderWriterLockSlim[] _pageLocks;
        protected MinidumpSegment _segmentData;
        protected volatile int _entrySize;
        private int _lastAccessTimestamp;
        private readonly int _minSize;
        private readonly Action<uint> _updateOwningCacheForAddedChunk;

        internal CacheEntryBase(MinidumpSegment segmentData, int derivedMinSize, Action<uint> updateOwningCacheForAddedChunk)
        {
            _segmentData = segmentData;

            int pageCount = (int)((_segmentData.End - _segmentData.VirtualAddress) / EntryPageSize);

            if ((_segmentData.End - _segmentData.VirtualAddress) % EntryPageSize != 0)
                pageCount++;

            _pages = new CachePage<T>[pageCount];
            _pageLocks = new ReaderWriterLockSlim[pageCount];
            for (int i = 0; i < _pageLocks.Length; i++)
            {
                _pageLocks[i] = new ReaderWriterLockSlim();
            }

            _minSize = 4 * UIntPtr.Size + /*our four fields that are reference type fields (pages, pageLocks, segmentData, and updateOwningCacheForAddedChunk)*/
                           2 * (_pages.Length * UIntPtr.Size) + /*The array of cache pages and matching size array of locks */
                           2 * sizeof(uint) + /*entrySize and minSize fields*/
                           sizeof(long) /*lastAccessTickCount field*/ +
                           derivedMinSize /*size added from our derived classes bookkeeping overhead*/;

            _entrySize = _minSize;

            _updateOwningCacheForAddedChunk = updateOwningCacheForAddedChunk;

            UpdateLastAccessTimstamp();
        }

        protected abstract uint EntryPageSize { get; }

        public override int CurrentSize
        {
            get
            {
                ThrowIfDisposed();
                return _entrySize;
            }
        }

        public override int MinSize
        {
            get
            {
                ThrowIfDisposed();
                return _minSize;
            }
        }

        public override int LastAccessTimestamp
        {
            get
            {
                ThrowIfDisposed();
                return _lastAccessTimestamp;
            }
        }

        public override void GetDataForAddress(ulong address, uint byteCount, IntPtr buffer, out uint bytesRead)
        {
            ThrowIfDisposed();

            ulong offset = address - _segmentData.VirtualAddress;

            int bytesRemaining = (int)byteCount;

            // NOTE: Seems silly to cache this here but it is a read of an abstract property and this method (and more specifically, ReadPageFromOffset who we pass this to) is the hottest
            // of hot methods for perf in this class.
            uint entryPageSize = EntryPageSize;

            ulong localBytesRead;
            do
            {
                ReadPageDataFromOffset(offset, buffer, (uint)bytesRemaining, entryPageSize, out localBytesRead);
                bytesRemaining -= (int)localBytesRead;

                buffer += (int)localBytesRead;
                offset += localBytesRead;
            } while (bytesRemaining > 0 && localBytesRead != 0);

            bytesRead = (byteCount - (uint)bytesRemaining);
        }

        public override bool GetDataFromAddressUntil(ulong address, byte[] terminatingSequence, out byte[] result)
        {
            ThrowIfDisposed();

            uint offset = (uint)(address - _segmentData.VirtualAddress);

            List<byte> bytesRead = new();
            bool res = ReadPageDataFromOffsetUntil(offset, terminatingSequence, bytesRead);

            result = bytesRead.ToArray();
            return res;
        }

        public override void UpdateLastAccessTimstamp()
        {
            // NOTE: It doesn't matter that this isn't interlocked/protected. This value simply indicates how recently accessed this entry is vis-a-vis some other
            // entry, if the values are slightly off because we have a RMW issue it doesn't matter since the only thing it influences is if this entry is eligible
            // to be trimmed, and being marginally off in the value is basically never going to put something into that group incorrectly. This USED to use
            // QPC/Interlocked.Exchange, but it showed up as a significant time sink in perf analysis.
            this._lastAccessTimestamp++;
        }

        public void Dispose()
        {
            if (_pages == null)
                return;

            Dispose(disposing: true);

            _pages = null;
            _pageLocks = null;
        }

        protected abstract void Dispose(bool disposing);

        protected void ThrowIfDisposed()
        {
            if (_pages == null)
                throw new ObjectDisposedException(GetType().Name);
        }

        private void ReadPageDataFromOffset(ulong segmentOffset, IntPtr buffer, uint byteCount, uint entryPageSize, out ulong bytesRead)
        {
            int pageIndex = (int)(segmentOffset / entryPageSize);

            // Request is past the end of our pages of data
            if (pageIndex >= _pages.Length)
            {
                bytesRead = 0;
                return;
            }

            ulong pageSegmentOffset = (ulong)pageIndex * entryPageSize;

            ulong inPageOffset = segmentOffset - pageSegmentOffset;

            bytesRead = ReadPageDataFromOffset(pageIndex, inPageOffset, byteCount, buffer, dataReader: null);
        }

        protected abstract uint InvokeCallbackWithDataPtr(CachePage<T> page, Func<UIntPtr, ulong, uint> callback);

        protected abstract uint CopyDataFromPage(CachePage<T> page, IntPtr buffer, ulong inPageOffset, uint byteCount);

        protected abstract (T Data, ulong DataExtent) GetPageDataAtOffset(ulong pageAlignedOffset);

        private ulong ReadPageDataFromOffset(int pageIndex, ulong inPageOffset, uint byteCount, IntPtr buffer, Func<UIntPtr, ulong, uint> dataReader)
        {
            bool notifyCacheOfSizeUpdate = false;

            ulong sizeRead = 0;
            int addedSize = 0;

            ReaderWriterLockSlim pageLock = _pageLocks[pageIndex];

            pageLock.EnterReadLock();
            bool holdsReadLock = true;
            try
            {
                // THREADING: If the data is not null we can just read it directly as we hold the read lock, if it is null we must acquire the write lock in
                // preparation to fetch the data from physical memory
                if (_pages[pageIndex] != null)
                {
                    UpdateLastAccessTimstamp();

                    if (dataReader == null)
                    {
                        sizeRead = CopyDataFromPage(_pages[pageIndex], buffer, inPageOffset, byteCount);
                    }
                    else
                    {
                        sizeRead = InvokeCallbackWithDataPtr(_pages[pageIndex], dataReader);
                    }
                }
                else
                {
                    pageLock.ExitReadLock();
                    holdsReadLock = false;

                    pageLock.EnterWriteLock();
                    try
                    {
                        // THREADING: Double check it's still null (i.e. no other thread beat us to paging this data in between dropping our read lock and acquiring
                        // the write lock)
                        if (_pages[pageIndex] == null)
                        {
                            ulong dataRange;
                            T data;
                            (data, dataRange) = GetPageDataAtOffset((ulong)pageIndex * EntryPageSize);

                            _pages[pageIndex] = new CachePage<T>(data, dataRange);

                            Interlocked.Add(ref _entrySize, (int)dataRange);

                            UpdateLastAccessTimstamp();

                            if (dataReader == null)
                            {
                                sizeRead = CopyDataFromPage(_pages[pageIndex], buffer, inPageOffset, byteCount);
                            }
                            else
                            {
                                sizeRead = InvokeCallbackWithDataPtr(_pages[pageIndex], dataReader);
                            }

                            addedSize = (int)dataRange;
                            notifyCacheOfSizeUpdate = true;
                        }
                        else
                        {
                            // Someone else beat us to retrieving the data, so we can just read
                            UpdateLastAccessTimstamp();

                            if (dataReader == null)
                            {
                                sizeRead = CopyDataFromPage(_pages[pageIndex], buffer, inPageOffset, byteCount);
                            }
                            else
                            {
                                sizeRead = InvokeCallbackWithDataPtr(_pages[pageIndex], dataReader);
                            }
                        }
                    }
                    finally
                    {
                        pageLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                if (holdsReadLock)
                    pageLock.ExitReadLock();
            }

            if (notifyCacheOfSizeUpdate)
            {
                _updateOwningCacheForAddedChunk((uint)addedSize);
            }

            return sizeRead;
        }

        private bool ReadPageDataFromOffsetUntil(uint segmentOffset, byte[] terminatingSequence, List<byte> bytesRead)
        {
            int pageIndex = (int)(segmentOffset / EntryPageSize);

            uint pageSegmentOffset = (uint)pageIndex * EntryPageSize;

            uint inPageOffset = (segmentOffset - pageSegmentOffset);

            bool sawTerminatingSequence = false;

            List<byte> trailingBytes = null;

            do
            {
                ReadPageDataFromOffset(pageIndex, inPageOffset, byteCount: 0, buffer: IntPtr.Zero, (data, dataLength) => {
                    return CacheEntryBase<T>.ProcessPageForSequenceTerminatingRead(data, dataLength, inPageOffset, terminatingSequence, bytesRead, ref trailingBytes, ref sawTerminatingSequence);
                });

                pageIndex++;
                inPageOffset = 0;
            } while ((pageIndex != _pages.Length) && !sawTerminatingSequence);

            return sawTerminatingSequence;
        }

        // This function processes a single page of data (pointed at by data) of length 'dataLength' copying bytes until it exhausts the page contents or encounters the given terminating sequence.
        // This is primarily useful for reading null-terminated strings when all you have is the start address and the knowledge it is a null-terminated string.
        //
        // A couple tricky things to keep in mind
        //
        // 1) The length of 'terminatingSequence' can be basically anything. For a a common null-terminator style read it is dependent on the encoding the string in memory is in, which only the caller
        //    knows.
        //
        // 2) We have to be careful if we have to read across a page boundary (or multiple), if the page isn't an even multiple of the terminating sequence length then we will have extra work to do,
        //    specifically we carry over the 'left over' bytes from the last page (in trailingBytes) and append to them data from the current page to see if that forms a terminating sequence. If so
        //    we are done, if not we have to copy the trailing bytes to the output (bytesRead) and skip the ones we added to check for a terminator when we start reading this page. The act of doing
        //    this could cascade and cause THIS page to also have 'trailing bytes', so we must continue this little adventure until the string terminates.
        private static unsafe uint ProcessPageForSequenceTerminatingRead(UIntPtr data,
                                                                         ulong dataLength,
                                                                         ulong inPageOffset,
                                                                         byte[] terminatingSequence,
                                                                         List<byte> bytesRead,
                                                                         ref List<byte> trailingBytes,
                                                                         ref bool sawTerminatingSequence)
        {
            uint dataRead = 0;

            ulong availableDataOnPage = dataLength - inPageOffset;
            uint startOffsetAdjustment = 0;

            if (trailingBytes != null && trailingBytes.Count != 0)
            {
                // We had trailing bytes on the last page's read, so we need to prepend enough bytes from the start of this read to trailing bytes to check if it forms a terminator
                // sequence, and if not copy the bytes over to the output buffer.

                // Since we are checking these bytes here make sure if we DON'T form a terminator sequence that the code below doesn't reprocess them. We know that trailingBytes.Count MUST be less
                // that terminatingSeqence.Length, if not we would have processed the trailing bytes in the last page and wouldn't see them here.
                startOffsetAdjustment = (uint)(terminatingSequence.Length - trailingBytes.Count);

                for (int i = 0; i < startOffsetAdjustment; i++)
                {
                    trailingBytes.Add(*((byte*)data + inPageOffset + i));
                }

                int j = 0;
                for (; j < terminatingSequence.Length; j++)
                {
                    if (trailingBytes[j] != terminatingSequence[j])
                        break;
                }

                if (j == terminatingSequence.Length)
                {
                    // we matched the whole terminating sequence with the trailing + leading bytes
                    sawTerminatingSequence = true;
                    return dataRead;
                }
                else
                {
                    // We didn't match the terminating sequence
                    bytesRead.AddRange(trailingBytes);
                    dataRead += (uint)trailingBytes.Count;
                    trailingBytes.Clear();
                }
            }

            // If we will have left over bytes (i.e. the amount to read mod the length of the terminating sequence is not 0), then copy then to the trailingBytes buffer so we can
            // process them on the next go around if we don't complete the read on this page.
            ulong leftoverByteCount = ((availableDataOnPage - startOffsetAdjustment) % (uint)terminatingSequence.Length);
            if (leftoverByteCount != 0)
            {
                // We will have straggling bytes if we don't complete the read on this page, so copy them to the trailingBytes list
                byte* pDataEnd = ((byte*)data + inPageOffset + availableDataOnPage);

                byte* pDataCur = (pDataEnd - leftoverByteCount);
                trailingBytes ??= new List<byte>((int)leftoverByteCount);

                while (pDataCur != pDataEnd)
                {
                    trailingBytes.Add(*pDataCur);
                    pDataCur++;
                }
            }

            // Account for any bytes we skipped above
            availableDataOnPage -= startOffsetAdjustment;

            for (uint i = 0 + startOffsetAdjustment; i < availableDataOnPage; i += (uint)terminatingSequence.Length)
            {
                uint j = 0;
                for (; j < terminatingSequence.Length; j++)
                {
                    if (*((byte*)data + inPageOffset + i + j) != terminatingSequence[j])
                        break;
                }

                if (j == terminatingSequence.Length)
                {
                    sawTerminatingSequence = true;
                    break;
                }
                else
                {
                    for (j = 0; j < terminatingSequence.Length; j++)
                    {
                        bytesRead.Add(*((byte*)data + inPageOffset + i + j));
                    }

                    dataRead += (uint)terminatingSequence.Length;
                }
            }

            return dataRead;
        }
    }
}