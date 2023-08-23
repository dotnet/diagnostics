// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal abstract class SegmentCacheEntry
    {
        public abstract int CurrentSize { get; }

        public abstract int MinSize { get; }

        public abstract int LastAccessTimestamp { get; }

        public abstract long PageOutData();

        public abstract void UpdateLastAccessTimstamp();

        public abstract void GetDataForAddress(ulong address, uint byteCount, IntPtr buffer, out uint bytesRead);

        public abstract bool GetDataFromAddressUntil(ulong address, byte[] terminatingSequence, out byte[] result);
    }
}