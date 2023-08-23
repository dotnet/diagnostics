// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.DacInterface;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class FullSyncBlock : SyncBlock
    {
        public override SyncBlockComFlags ComFlags { get; }

        public override bool IsComCallWrapper => (ComFlags & SyncBlockComFlags.ComCallableWrapper) == SyncBlockComFlags.ComCallableWrapper;
        public override bool IsRuntimeCallWrapper => (ComFlags & SyncBlockComFlags.RuntimeCallableWrapper) == SyncBlockComFlags.RuntimeCallableWrapper;
        public override bool IsComClassFactory => (ComFlags & SyncBlockComFlags.ComClassFactory) == SyncBlockComFlags.ComClassFactory;

        public override bool IsMonitorHeld { get; }
        public override ulong HoldingThreadAddress { get; }
        public override int RecursionCount { get; }
        public override int WaitingThreadCount { get; }

        public FullSyncBlock(in SyncBlockData syncBlk, int index)
            : base(syncBlk.Object, index)
        {
            ComFlags = (SyncBlockComFlags)syncBlk.COMFlags;

            IsMonitorHeld = syncBlk.MonitorHeld != 0;
            HoldingThreadAddress = syncBlk.HoldingThread;
            RecursionCount = syncBlk.Recursion >= int.MaxValue ? int.MaxValue : (int)syncBlk.Recursion;
            // https://stackoverflow.com/questions/2203000/windbg-sos-explanation-of-syncblk-output
            WaitingThreadCount = IsMonitorHeld ? (int)((syncBlk.MonitorHeld - 1) / 2) : (int)syncBlk.AdditionalThreadCount;
        }
    }
}
