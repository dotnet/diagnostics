// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrThread : IEquatable<IClrThread>
    {
        ulong Address { get; }
        IClrAppDomain? CurrentAppDomain { get; }
        IClrException? CurrentException { get; }
        GCMode GCMode { get; }
        bool IsAlive { get; }
        uint LockCount { get; }
        int ManagedThreadId { get; }
        uint OSThreadId { get; }
        IClrRuntime Runtime { get; }
        ulong StackBase { get; }
        ulong StackLimit { get; }
        ClrThreadState State { get; }
        bool IsFinalizer { get; }
        bool IsGc { get; }
        IEnumerable<IClrRoot> EnumerateStackRoots();
        IEnumerable<IClrStackFrame> EnumerateStackTrace(bool includeContext = false);
    }
}
