// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal interface IClrTypeHelpers
    {
        CacheOptions CacheOptions { get; }
        ClrHeap Heap { get; }
        IDataReader DataReader { get; }

        bool TryGetTypeName(ClrType type, out string? name);
        ulong GetLoaderAllocatorHandle(ulong mt);
        ulong GetAssemblyLoadContextAddress(ulong mt);

        string? ReadString(ulong addr, int maxLength);
        ComCallableWrapper? CreateCCWForObject(ulong obj);
        RuntimeCallableWrapper? CreateRCWForObject(ulong obj);
        ImmutableArray<ComInterfaceData> GetRCWInterfaces(ulong address, int interfaceCount);
        ClrType? CreateRuntimeType(ClrObject obj);

        ulong GetObjectDataPointer(ulong objRef);
        ClrElementType GetObjectElementType(ulong objRef);
        ImmutableArray<ClrMethod> GetMethodsForType(ClrType type);
        IEnumerable<ClrField> EnumerateFields(ClrType type);
    }
}