// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrJitManager
    {
        ulong Address { get; }
        CodeHeapKind Kind { get; }
        IClrRuntime Runtime { get; }

        IEnumerable<ClrNativeHeapInfo> EnumerateNativeHeaps();
    }
}
