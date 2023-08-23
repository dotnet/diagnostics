// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    /// <summary>
    /// This represents a ClrType for which we cannot get information from the dac.  In theory we shouldn't need this
    /// type, but in practice there are fields which do not report a type.  This allows us to provide a non-null, semi
    /// meaningful type even though it's not as accurate or specific as we wish it would be.
    /// </summary>
    internal sealed class ClrGenericType : ClrType
    {
        public ClrGenericParameter GenericParameter { get; }

        public ClrGenericType(IClrTypeHelpers helpers, ClrHeap heap, ClrModule? module, ClrGenericParameter clrGenericParameter)
            : base(helpers)
        {
            Heap = heap ?? throw new ArgumentNullException(nameof(heap));
            Module = module;
            GenericParameter = clrGenericParameter;
            _fields = ImmutableArray<ClrInstanceField>.Empty;
            _staticFields = ImmutableArray<ClrStaticField>.Empty;
        }

        public override GCDesc GCDesc => default;

        public override ulong MethodTable => 0;

        public override int MetadataToken => GenericParameter.MetadataToken;

        public override string? Name => GenericParameter.Name;

        public override ClrHeap Heap { get; }

        public override ClrModule? Module { get; }

        public override ClrElementType ElementType => ClrElementType.Var;

        public override bool IsFinalizable => false;

        public override TypeAttributes TypeAttributes => TypeAttributes.Public;

        public override ClrType? BaseType => null;

        public override ClrType? ComponentType => null;

        public override bool IsArray => false;

        public override int StaticSize => 0;

        public override int ComponentSize => 0;

        public override bool IsEnum => false;

        public override bool IsShared => false;

        public override ClrEnum AsEnum() => throw new InvalidOperationException();

        public override IEnumerable<ClrInterface> EnumerateInterfaces() { yield break; }

        public override ulong GetArrayElementAddress(ulong objRef, int index) => throw new InvalidOperationException();

        public override ClrInstanceField? GetFieldByName(string name) => null;

        public override ClrStaticField? GetStaticFieldByName(string name) => null;

        public override bool IsFinalizeSuppressed(ulong obj) => false;

        public override T[]? ReadArrayElements<T>(ulong objRef, int start, int count) => null;
    }
}
