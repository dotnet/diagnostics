// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SyncBlockCleanupData
    {
        public readonly ClrDataAddress SyncBlockPointer;
        public readonly ClrDataAddress NextSyncBlock;
        public readonly ClrDataAddress BlockRCW;
        public readonly ClrDataAddress BlockClassFactory;
        public readonly ClrDataAddress BlockCCW;
    }
}
