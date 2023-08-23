// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal interface IClrRuntimeHelpers : IDisposable
    {
        void Flush();
        IEnumerable<ClrThread> EnumerateThreads();
        ClrHeap CreateHeap();
        ClrAppDomainData GetAppDomainData();
        ClrMethod? GetMethodByMethodDesc(ulong methodDesc);
        ClrMethod? GetMethodByInstructionPointer(ulong ip);
        IEnumerable<ClrHandle> EnumerateHandles();
        IEnumerable<ClrJitManager> EnumerateClrJitManagers();
        string? GetJitHelperFunctionName(ulong address);
        ClrThreadPool? GetThreadPool();
        IEnumerable<ClrNativeHeapInfo> EnumerateGCFreeRegions();
        IEnumerable<ClrNativeHeapInfo> EnumerateHandleTableRegions();
        IEnumerable<ClrNativeHeapInfo> EnumerateGCBookkeepingRegions();
        IEnumerable<ClrSyncBlockCleanupData> EnumerateSyncBlockCleanupData();
        IEnumerable<ClrRcwCleanupData> EnumerateRcwCleanupData();
    }
}
