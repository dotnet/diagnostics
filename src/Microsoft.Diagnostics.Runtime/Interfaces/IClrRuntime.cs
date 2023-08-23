// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrRuntime : IDisposable
    {
        ImmutableArray<IClrAppDomain> AppDomains { get; }
        IClrModule BaseClassLibrary { get; }
        IClrInfo ClrInfo { get; }
        IDataTarget DataTarget { get; }
        IClrHeap Heap { get; }
        bool IsThreadSafe { get; }
        IClrAppDomain? SharedDomain { get; }
        IClrAppDomain? SystemDomain { get; }
        ImmutableArray<IClrThread> Threads { get; }

        IEnumerable<ClrNativeHeapInfo> EnumerateClrNativeHeaps();
        IEnumerable<IClrRoot> EnumerateHandles();
        IEnumerable<IClrJitManager> EnumerateJitManagers();
        IEnumerable<IClrModule> EnumerateModules();
        void FlushCachedData();
        string? GetJitHelperFunctionName(ulong address);
        IClrMethod? GetMethodByHandle(ulong methodHandle);
        IClrMethod? GetMethodByInstructionPointer(ulong ip);
        IClrType? GetTypeByMethodTable(ulong methodTable);
        IEnumerable<ClrSyncBlockCleanupData> EnumerateSyncBlockCleanupData();
        IEnumerable<ClrRcwCleanupData> EnumerateRcwCleanupData();

        /// <summary>
        /// Gets information about CLR's ThreadPool.  May return null if we could not obtain
        /// ThreadPool data from the target process or dump.
        /// </summary>
        IClrThreadPool? ThreadPool { get; }
    }
}
