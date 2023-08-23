// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal interface ICcwData
    {
        ulong Address { get; }
        ulong IUnknown { get; }
        ulong Object { get; }
        ulong Handle { get; }
        int RefCount { get; }
        int JupiterRefCount { get; }

        ImmutableArray<ComInterfaceData> GetInterfaces();
    }
}
