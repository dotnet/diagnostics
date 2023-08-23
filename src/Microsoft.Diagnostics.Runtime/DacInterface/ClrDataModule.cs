// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class ClrDataModule : CallableCOMWrapper
    {
        private const uint DACDATAMODULEPRIV_REQUEST_GET_MODULEDATA = 0xf0000001;

        private static readonly Guid IID_IXCLRDataModule = new("88E32849-0A0A-4cb0-9022-7CD2E9E139E2");

        public ClrDataModule(DacLibrary library, IntPtr pUnknown)
            : base(library?.OwningLibrary, IID_IXCLRDataModule, pUnknown)
        {
        }

        private ref readonly IClrDataModuleVTable VTable => ref Unsafe.AsRef<IClrDataModuleVTable>(_vtable);

        public HResult GetModuleData(out ExtendedModuleData data)
        {
            fixed (void* dataPtr = &data)
            {
                HResult hr = VTable.Request(Self, DACDATAMODULEPRIV_REQUEST_GET_MODULEDATA, 0, null, sizeof(ExtendedModuleData), dataPtr);
                if (!hr)
                    data = default;

                return hr;
            }
        }

        public string? GetName()
        {
            HResult hr = VTable.GetName(Self, 0, out int nameLength, null);
            if (!hr)
                return null;

            string name = new('\0', nameLength - 1);
            fixed (char* namePtr = name)
                hr = VTable.GetName(Self, nameLength, out _, namePtr);

            return hr ? name : null;
        }

        public string? GetFileName()
        {
            // GetFileName will fault if buffer pointer is null. Use fixed size buffer.
            char[] buffer = ArrayPool<char>.Shared.Rent(1024);
            try
            {
                fixed (char* bufferPtr = buffer)
                {
                    HResult hr = VTable.GetFileName(Self, buffer.Length, out int nameLength, bufferPtr);
                    return hr && nameLength > 0 ? new string(buffer, 0, nameLength - 1) : null;
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly unsafe struct IClrDataModuleVTable
        {
            private readonly IntPtr StartEnumAssemblies;
            private readonly IntPtr EnumAssembly;
            private readonly IntPtr EndEnumAssemblies;
            private readonly IntPtr StartEnumTypeDefinitions;
            private readonly IntPtr EnumTypeDefinition;
            private readonly IntPtr EndEnumTypeDefinitions;
            private readonly IntPtr StartEnumTypeInstances;
            private readonly IntPtr EnumTypeInstance;
            private readonly IntPtr EndEnumTypeInstances;
            private readonly IntPtr StartEnumTypeDefinitionsByName;
            private readonly IntPtr EnumTypeDefinitionByName;
            private readonly IntPtr EndEnumTypeDefinitionsByName;
            private readonly IntPtr StartEnumTypeInstancesByName;
            private readonly IntPtr EnumTypeInstanceByName;
            private readonly IntPtr EndEnumTypeInstancesByName;
            private readonly IntPtr GetTypeDefinitionByToken;
            private readonly IntPtr StartEnumMethodDefinitionsByName;
            private readonly IntPtr EnumMethodDefinitionByName;
            private readonly IntPtr EndEnumMethodDefinitionsByName;
            private readonly IntPtr StartEnumMethodInstancesByName;
            private readonly IntPtr EnumMethodInstanceByName;
            private readonly IntPtr EndEnumMethodInstancesByName;
            private readonly IntPtr GetMethodDefinitionByToken;
            private readonly IntPtr StartEnumDataByName;
            private readonly IntPtr EnumDataByName;
            private readonly IntPtr EndEnumDataByName;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out int, char*, int> GetName;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out int, char*, int> GetFileName;
            private readonly IntPtr GetFlags;
            private readonly IntPtr IsSameObject;
            private readonly IntPtr StartEnumExtents;
            private readonly IntPtr EnumExtent;
            private readonly IntPtr EndEnumExtents;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, int, void*, int, void*, int> Request;
            private readonly IntPtr StartEnumAppDomains;
            private readonly IntPtr EnumAppDomain;
            private readonly IntPtr EndEnumAppDomains;
            private readonly IntPtr GetVersionId;
        }
    }
}