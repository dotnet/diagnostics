// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IComCallableWrapper
    {
        ulong Address { get; }
        ulong Handle { get; }
        ImmutableArray<ComInterfaceData> Interfaces { get; }
        ulong IUnknown { get; }
        ulong Object { get; }
        int RefCount { get; }
    }
}