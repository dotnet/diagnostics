// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrOutOfMemoryInfo
    {
        OutOfMemoryReason Reason { get; }
        GetMemoryFailureReason GetMemoryFailure { get; }
        bool IsLargeObjectHeap { get; }
        ulong AllocSize { get; }
        ulong AvailablePageFileMB { get; }
        ulong GCIndex { get; }
        ulong Size { get; }
    }
}