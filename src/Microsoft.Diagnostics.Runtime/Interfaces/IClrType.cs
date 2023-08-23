// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrType : IEquatable<IClrType>
    {
        ulong AssemblyLoadContextAddress { get; }
        IClrType? BaseType { get; }
        int ComponentSize { get; }
        IClrType? ComponentType { get; }
        bool ContainsPointers { get; }
        ClrElementType ElementType { get; }
        ImmutableArray<IClrInstanceField> Fields { get; }
        GCDesc GCDesc { get; }
        IClrHeap Heap { get; }
        bool IsArray { get; }
        bool IsCollectible { get; }
        bool IsEnum { get; }
        bool IsException { get; }
        bool IsFinalizable { get; }
        bool IsFree { get; }
        bool IsObjectReference { get; }
        bool IsPointer { get; }
        bool IsPrimitive { get; }
        bool IsShared { get; }
        bool IsString { get; }
        bool IsValueType { get; }
        ulong LoaderAllocatorHandle { get; }
        int MetadataToken { get; }
        ImmutableArray<IClrMethod> Methods { get; }
        ulong MethodTable { get; }
        IClrModule? Module { get; }
        string? Name { get; }
        ImmutableArray<IClrStaticField> StaticFields { get; }
        int StaticSize { get; }
        TypeAttributes TypeAttributes { get; }

        IClrEnum AsEnum();
        IEnumerable<ClrGenericParameter> EnumerateGenericParameters();
        IEnumerable<ClrInterface> EnumerateInterfaces();
        ulong GetArrayElementAddress(ulong objRef, int index);
        IClrInstanceField? GetFieldByName(string name);
        int GetHashCode();
        IClrStaticField? GetStaticFieldByName(string name);
        bool IsFinalizeSuppressed(ulong obj);
        T[]? ReadArrayElements<T>(ulong objRef, int start, int count) where T : unmanaged;
    }
}
