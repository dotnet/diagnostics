// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class SyncBlockContainer : IEnumerable<SyncBlock>
    {
        private readonly SyncBlock[] _syncBlocks;
        private readonly Dictionary<ulong, SyncBlock> _mapping = new();

        public int Count => _syncBlocks.Length;
        public SyncBlock this[int index] => _syncBlocks[index];
        public SyncBlock this[uint index] => _syncBlocks[index];

        public SyncBlockContainer(IEnumerable<SyncBlock> syncBlocks)
        {
            _syncBlocks = syncBlocks.ToArray();
            foreach (SyncBlock item in _syncBlocks)
            {
                if (item.Object != 0)
                    _mapping[item.Object] = item;
            }
        }

        public SyncBlock? TryGetSyncBlock(ulong obj)
        {
            _mapping.TryGetValue(obj, out SyncBlock? result);
            return result;
        }

        public IEnumerator<SyncBlock> GetEnumerator()
        {
            return ((IEnumerable<SyncBlock>)_syncBlocks).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _syncBlocks.GetEnumerator();
        }
    }
}