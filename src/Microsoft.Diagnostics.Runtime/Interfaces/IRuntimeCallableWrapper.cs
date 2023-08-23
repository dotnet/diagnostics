// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime
{
    public interface IRuntimeCallableWrapper
    {
        ulong Address { get; }
        ulong CreatorThreadAddress { get; }
        ImmutableArray<ComInterfaceData> Interfaces { get; }
        bool IsDisconnected { get; }
        ulong IUnknown { get; }
        ulong Object { get; }
        int RefCount { get; }
        ulong VTablePointer { get; }
        ulong WinRTObject { get; }
    }
}
