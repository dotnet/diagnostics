// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal interface ISOSDac13 : IDisposable
    {
        ClrDataAddress GetDomainLoaderAllocator(ClrDataAddress domainAddress);
        SosMemoryEnum? GetGCBookkeepingMemoryRegions();
        SosMemoryEnum? GetGCFreeRegions();
        SosMemoryEnum? GetHandleTableRegions();
        string[] GetLoaderAllocatorHeapNames();
        (ClrDataAddress Address, SOSDac13.LoaderHeapKind Kind)[] GetLoaderAllocatorHeaps(ClrDataAddress loaderAllocator);
        bool LockedFlush();
        HResult TraverseLoaderHeap(ulong heap, SOSDac13.LoaderHeapKind kind, SOSDac.LoaderHeapTraverse callback);
    }
}