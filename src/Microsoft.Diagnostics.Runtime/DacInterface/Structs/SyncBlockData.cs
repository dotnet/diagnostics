// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SyncBlockData
    {
        public readonly ClrDataAddress Object;
        public readonly uint Free;
        public readonly ClrDataAddress Address;
        public readonly uint COMFlags;
        public readonly uint MonitorHeld;
        public readonly uint Recursion;
        public readonly ClrDataAddress HoldingThread;
        public readonly uint AdditionalThreadCount;
        public readonly ClrDataAddress AppDomain;
        public readonly uint TotalSyncBlockCount;
    }
}