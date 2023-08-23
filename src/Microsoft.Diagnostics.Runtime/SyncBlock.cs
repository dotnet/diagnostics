// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    public class SyncBlock
    {
        public ulong Object { get; }
        public int Index { get; }

        public virtual SyncBlockComFlags ComFlags => SyncBlockComFlags.None;
        public virtual bool IsComCallWrapper => false;
        public virtual bool IsRuntimeCallWrapper => false;
        public virtual bool IsComClassFactory => false;

        public virtual bool IsMonitorHeld => false;
        public virtual ulong HoldingThreadAddress => 0;
        public virtual int RecursionCount => 0;
        public virtual int WaitingThreadCount => 0;

        public SyncBlock(ulong obj, int index)
        {
            Object = obj;
            Index = index;
        }
    }
}