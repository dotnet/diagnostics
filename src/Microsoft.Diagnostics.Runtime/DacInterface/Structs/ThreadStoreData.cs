// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ThreadStoreData
    {
        public readonly int ThreadCount;
        public readonly int UnstartedThreadCount;
        public readonly int BackgroundThreadCount;
        public readonly int PendingThreadCount;
        public readonly int DeadThreadCount;
        public readonly ClrDataAddress FirstThread;
        public readonly ClrDataAddress FinalizerThread;
        public readonly ClrDataAddress GCThread;
        public readonly uint HostConfig;
    }
}
