// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Diagnostics.Runtime.DacInterface;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ClrStringType : ClrType
    {
        public ClrStringType(IClrTypeHelpers helpers, ClrHeap heap, ulong mt, int token)
            : base(helpers)
        {
            Heap = heap;
            MethodTable = mt;

            MetadataToken = token;
        }

        public override GCDesc GCDesc => default;

        public override ulong MethodTable { get; }

        public override int MetadataToken { get; }

        public override string? Name => "System.String";

        public override ClrHeap Heap { get; }

        public override ClrModule? Module => Heap.Runtime.BaseClassLibrary;

        public override ClrElementType ElementType => ClrElementType.String;

        public override bool ContainsPointers => false;

        public override bool IsFinalizable => true;

        public override TypeAttributes TypeAttributes => TypeAttributes.Public;

        public override ClrType? BaseType => Heap.ObjectType;

        public override ClrType? ComponentType => null;

        public override bool IsArray => false;

        public override int StaticSize => IntPtr.Size + sizeof(int);

        public override int ComponentSize => sizeof(char);

        public override bool IsEnum => false;

        public override bool IsShared => true;

        public override bool IsString => true;

        public override ClrEnum AsEnum() => throw new InvalidOperationException($"{Name ?? nameof(ClrType)} is not an enum.  You must call {nameof(ClrType.IsEnum)} before using {nameof(AsEnum)}.");

        public override IEnumerable<ClrInterface> EnumerateInterfaces()
        {
            MetadataImport? import = Module?.MetadataImport;
            if (import is null)
                yield break;

            foreach (int token in import.EnumerateInterfaceImpls(MetadataToken))
            {
                if (import.GetInterfaceImplProps(token, out _, out int mdIFace))
                {
                    ClrInterface? result = GetInterface(import, mdIFace);
                    if (result != null)
                        yield return result;
                }
            }
        }

        private ClrInterface? GetInterface(MetadataImport import, int mdIFace)
        {
            ClrInterface? result = null;
            if (!import.GetTypeDefProperties(mdIFace, out string? name, out _, out int extends).IsOK)
            {
                name = import.GetTypeRefName(mdIFace);
            }

            // TODO:  Handle typespec case.
            if (name != null)
            {
                ClrInterface? type = null;
                if (extends is not 0 and not 0x01000000)
                    type = GetInterface(import, extends);

                result = new ClrInterface(name, type);
            }

            return result;
        }

        public override ulong GetArrayElementAddress(ulong objRef, int index)
        {
            throw new NotImplementedException();
        }

        public override T[]? ReadArrayElements<T>(ulong objRef, int start, int count)
        {
            throw new NotImplementedException();
        }

        // TODO: remove
        public override ClrStaticField? GetStaticFieldByName(string name) => StaticFields.FirstOrDefault(f => f.Name == name);

        // TODO: remove
        public override ClrInstanceField? GetFieldByName(string name) => Fields.FirstOrDefault(f => f.Name == name);

        private const uint FinalizationSuppressedFlag = 0x40000000;
        public override bool IsFinalizeSuppressed(ulong obj)
        {
            // TODO move to ClrObject?
            uint value = Helpers.DataReader.Read<uint>(obj - 4);

            return (value & FinalizationSuppressedFlag) == FinalizationSuppressedFlag;
        }
    }
}