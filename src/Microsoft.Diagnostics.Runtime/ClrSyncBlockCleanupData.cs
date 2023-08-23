// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    public class ClrSyncBlockCleanupData
    {
        public ClrSyncBlockCleanupData(ulong syncBlockPointer, ulong blockRCW, ulong blockCCW, ulong blockClassFactory)
        {
            SyncBlock = syncBlockPointer;
            Rcw = blockRCW;
            Ccw = blockCCW;
            ClassFactory = blockClassFactory;
        }

        public ulong SyncBlock { get; }
        public ulong Rcw { get; }
        public ulong Ccw { get; }
        public ulong ClassFactory { get; }
    }
}
