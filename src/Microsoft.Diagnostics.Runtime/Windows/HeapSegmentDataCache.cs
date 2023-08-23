// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

// TODO:  This code wasn't written to consider nullable.
#nullable disable

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal sealed class HeapSegmentDataCache : IDisposable
    {
        private ReaderWriterLockSlim _cacheLock = new();
        private Dictionary<ulong, SegmentCacheEntry> _cache = new();
        private SegmentCacheEntryFactory _entryFactory;

        private long _cacheSize;
        private readonly long _maxSize;
        private readonly uint _entryCountWhenFull;
        private bool _cacheIsComplete;
        private readonly bool _cacheIsFullyPopulatedBeforeUse;

        public HeapSegmentDataCache(SegmentCacheEntryFactory entryFactory, uint entryCountWhenFull, bool cacheIsFullyPopulatedBeforeUse, long maxSize)
        {
            _entryFactory = entryFactory;
            _maxSize = maxSize;
            _entryCountWhenFull = entryCountWhenFull;
            _cacheIsFullyPopulatedBeforeUse = cacheIsFullyPopulatedBeforeUse;
        }

        public SegmentCacheEntry CreateAndAddEntry(MinidumpSegment segment)
        {
            ThrowIfDisposed();

            if (_cacheIsComplete)
                throw new InvalidOperationException($"You cannot call {nameof(CreateAndAddEntry)} after having called {nameof(CreateAndAddEntry)} enough times to cause the entry count to rise to {_entryCountWhenFull}, which was given to the ctor as the largest possible size");

            SegmentCacheEntry entry = _entryFactory.CreateEntryForSegment(segment, UpdateOverallCacheSizeForAddedChunk);

            if (!_cacheIsFullyPopulatedBeforeUse)
                _cacheLock.EnterWriteLock();

            try
            {
                // Check the cache again now that we have acquired the write lock
                if (_cache.TryGetValue(segment.VirtualAddress, out SegmentCacheEntry existingEntry))
                {
                    // Someone else beat us to adding this entry, clean up the entry we created and return the existing one
                    using (entry as IDisposable)
                        return existingEntry;
                }

                _cache.Add(segment.VirtualAddress, entry);
                _cacheIsComplete = (_cache.Count == _entryCountWhenFull);
            }
            finally
            {
                if (!_cacheIsFullyPopulatedBeforeUse)
                    _cacheLock.ExitWriteLock();
            }

            Interlocked.Add(ref _cacheSize, entry.CurrentSize);
            TrimCacheIfOverLimit();

            return entry;
        }

        public bool TryGetCacheEntry(ulong baseAddress, out SegmentCacheEntry entry)
        {
            ThrowIfDisposed();

            bool acquiredReadLock = false;

            if (!_cacheIsComplete)
            {
                _cacheLock.EnterReadLock();
                acquiredReadLock = true;
            }

            bool res = false;

            try
            {
                res = _cache.TryGetValue(baseAddress, out entry);
            }
            finally
            {
                if (acquiredReadLock)
                    _cacheLock.ExitReadLock();
            }

            if (res)
            {
                entry?.UpdateLastAccessTimstamp();
            }

            return res;
        }

        public void Dispose()
        {
            if (_cache == null)
                return;

            using (_entryFactory as IDisposable)
            {
                _cacheLock.EnterWriteLock();
                try
                {
                    foreach (KeyValuePair<ulong, SegmentCacheEntry> kvp in _cache)
                    {
                        (kvp.Value as IDisposable)?.Dispose();
                    }

                    _cache.Clear();
                }
                finally
                {
                    _cacheLock.ExitWriteLock();
                    _cacheLock.Dispose();

                    _cacheLock = null;
                    _cache = null;
                    _entryFactory = null;
                }
            }
        }

        private void UpdateOverallCacheSizeForAddedChunk(uint chunkSize)
        {
            ThrowIfDisposed();

            Interlocked.Add(ref _cacheSize, chunkSize);

            TrimCacheIfOverLimit();
        }

        private void TrimCacheIfOverLimit()
        {
            if (Interlocked.Read(ref _cacheSize) < _maxSize)
                return;

            IList<(KeyValuePair<ulong, SegmentCacheEntry> CacheEntry, int LastAccessTimestamp)> entries = SnapshotNonMinSizeCacheItems();
            if (entries.Count == 0)
                return;

            // Try to cut ourselves down to about 85% of our max capacity, otherwise just hang out right at that boundary and the next entry we add we end up having to
            // scavenge again, and again, and again...
            uint requiredCutAmount = (uint)(_maxSize * 0.15);

            long desiredSize = (long)(_maxSize * 0.85);
            bool haveUpdatedSnapshot = false;

            uint cutAmount = 0;
            while (cutAmount < requiredCutAmount)
            {
                // We could also be trimming on other threads, so if collectively we have brought ourselves below 85% of our max capacity then we are done
                if (Interlocked.Read(ref _cacheSize) <= desiredSize)
                    break;

                // find the largest item of the 10% of least recently accessed (remaining) items
                uint largestSizeSeen = 0;
                int curItemIndex = (entries.Count - 1) - (int)(entries.Count * 0.10);

                if (curItemIndex < 0)
                    return;

                int removalTargetIndex = -1;
                while (curItemIndex < entries.Count)
                {
                    KeyValuePair<ulong, SegmentCacheEntry> curItem = entries[curItemIndex].CacheEntry;

                    // TODO: CurrentSize is constantly in flux, other threads are paging data in and out, so while I can read it in the below check there is no guarantee that by the time I get
                    // to the code that tries to REMOVE this item that CurrentSize hasn't changed
                    //
                    // >= so we prefer the largest item that is least recently accessed, ensuring we don't remove a segment that is being actively modified now (should
                    // never happen since we also update that segments accessed timestamp, but, defense in depth).
                    if ((curItem.Value.CurrentSize - curItem.Value.MinSize) >= largestSizeSeen)
                    {
                        largestSizeSeen = (uint)(curItem.Value.CurrentSize - curItem.Value.MinSize);
                        removalTargetIndex = curItemIndex;
                    }

                    curItemIndex++;
                }

                if (removalTargetIndex < 0)
                {
                    // If we are already below our desired size or we have already re-snapshotted one time, then just give up
                    if (haveUpdatedSnapshot || (Interlocked.Read(ref _cacheSize) <= desiredSize))
                        break;

                    // we failed to find any non MinSize entries in the last 10% of entries. Since our snapshot originally ONLY contained non-MinSize entries this likely
                    // means other threads are also trimming. Re-snapshot and try again.
                    entries = SnapshotNonMinSizeCacheItems();
                    haveUpdatedSnapshot = true;

                    if (entries.Count == 0)
                        break;

                    continue;
                }

                SegmentCacheEntry targetItem = entries[removalTargetIndex].CacheEntry.Value;

                if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                    HeapSegmentCacheEventSource.Instance.PageOutDataStart();

                long sizeRemoved = targetItem.PageOutData();

                if (HeapSegmentCacheEventSource.Instance.IsEnabled())
                    HeapSegmentCacheEventSource.Instance.PageOutDataEnd(sizeRemoved);

                // Whether or not we managed to remove any memory for this item (another thread may have removed it all before we could), remove it from our list of
                // entries to consider
                entries.RemoveAt(removalTargetIndex);

                if (sizeRemoved != 0)
                {
                    Interlocked.Add(ref _cacheSize, -sizeRemoved);
                    cutAmount += (uint)sizeRemoved;
                }
            }
        }

        private IList<(KeyValuePair<ulong, SegmentCacheEntry> CacheEntry, int LastAccessTimestamp)> SnapshotNonMinSizeCacheItems()
        {
            IEnumerable<(KeyValuePair<ulong, SegmentCacheEntry> CacheEntry, int LastAccessTimestamp)> items = null;
            List<(KeyValuePair<ulong, SegmentCacheEntry> CacheEntry, int LastAccessTimestamp)> entries = null;

            bool acquiredReadLock = false;

            if (!_cacheIsComplete)
            {
                _cacheLock.EnterReadLock();
                acquiredReadLock = true;
            }

            // Select all cache entries which aren't at their min-size
            //
            // THREADING: We snapshot the LastAccessTickCount values here because there is the case where the Sort function will throw an exception if it tests two entries and the
            // lhs rhs comparison is inconsistent when reversed (i.e. something like lhs < rhs is true but then rhs < lhs is also true). This sound illogical BUT it can happen
            // if between the two comparisons the LastAccessTickCount changes (because other threads are concurrently accessing these same entries), in that case we would trigger
            // this exception, which is bad :)
            //
            // TODO: CurrentSize is constantly in flux, other threads are paging data in and out, so while I can read it in the below Where call there is no guarantee that by the time I get
            // to the code that looks for the largest item that CurrentSize hasn't changed
            try
            {
                items = _cache.Where((kvp) => kvp.Value.CurrentSize != kvp.Value.MinSize).Select((kvp) => (CacheEntry: kvp, kvp.Value.LastAccessTimestamp));
                entries = new List<(KeyValuePair<ulong, SegmentCacheEntry> CacheEntry, int LastAccessTimestamp)>(items);
            }
            finally
            {
                if (acquiredReadLock)
                    _cacheLock.ExitReadLock();
            }

            // Flip the sort order to the LEAST recently accessed items (i.e. the ones whose LastAccessTickCount are furthest in history) end up at the END of the array,
            //
            // NOTE: Using tickcounts is susceptible to roll-over, but worst case scenario we remove a slightly more recently used one thinking it is older, not a huge deal
            // and using DateTime.Now to get a non-roll-over susceptible timestamp showed up as 5% of scenario time in PerfView :(
            entries.Sort((lhs, rhs) => rhs.LastAccessTimestamp.CompareTo(lhs.LastAccessTimestamp));

            return entries;
        }

        private void ThrowIfDisposed()
        {
            if (_cache == null)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}