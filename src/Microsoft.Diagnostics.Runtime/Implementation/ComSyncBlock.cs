// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ComSyncBlock : SyncBlock
    {
        public override SyncBlockComFlags ComFlags { get; }

        public override bool IsComCallWrapper => (ComFlags & SyncBlockComFlags.ComCallableWrapper) == SyncBlockComFlags.ComCallableWrapper;
        public override bool IsRuntimeCallWrapper => (ComFlags & SyncBlockComFlags.ComCallableWrapper) == SyncBlockComFlags.ComCallableWrapper;
        public override bool IsComClassFactory => (ComFlags & SyncBlockComFlags.ComClassFactory) == SyncBlockComFlags.ComClassFactory;

        public ComSyncBlock(ulong obj, int index, uint comFlags)
            : base(obj, index)
        {
            ComFlags = (SyncBlockComFlags)comFlags;
        }
    }
}
