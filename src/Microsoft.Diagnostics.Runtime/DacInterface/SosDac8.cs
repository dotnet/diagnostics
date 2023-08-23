// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    /// <summary>
    /// This is an undocumented, untested, and unsupported interface.  Do not use.
    /// </summary>
    internal sealed unsafe class SOSDac8 : CallableCOMWrapper
    {
        internal static readonly Guid IID_ISOSDac8 = new("c12f35a9-e55c-4520-a894-b3dc5165dfce");

        public SOSDac8(DacLibrary library, IntPtr ptr)
            : base(library?.OwningLibrary, IID_ISOSDac8, ptr)
        {
        }

        private ref readonly ISOSDac8VTable VTable => ref Unsafe.AsRef<ISOSDac8VTable>(_vtable);

        public HResult GetAssemblyLoadContext(ClrDataAddress methodTable, out ClrDataAddress assemblyLoadContext)
        {
            return VTable.GetAssemblyLoadContext(Self, methodTable, out assemblyLoadContext);
        }

        public int GenerationCount
        {
            get
            {
                HResult hr = VTable.GetNumberGenerations(Self, out int generations);
                if (hr)
                    return generations;

                return 0;
            }
        }

        public GenerationData[]? GetGenerationTable()
        {
            HResult hr = VTable.GetGenerationTable(Self, 0, null, out int needed);
            if (!hr)
                return null;

            GenerationData[] data = new GenerationData[needed];
            fixed (GenerationData* ptr = data)
            {
                hr = VTable.GetGenerationTable(Self, needed, ptr, out _);
                if (!hr)
                    return null;
            }

            return data;
        }

        public GenerationData[]? GetGenerationTable(ulong heap)
        {
            HResult hr = VTable.GetGenerationTableSvr(Self, heap, 0, null, out int needed);
            if (!hr)
                return null;

            GenerationData[] data = new GenerationData[needed];
            fixed (GenerationData* ptr = data)
            {
                hr = VTable.GetGenerationTableSvr(Self, heap, needed, ptr, out _);
                if (!hr)
                    return null;
            }

            return data;
        }

        public ClrDataAddress[]? GetFinalizationFillPointers()
        {
            HResult hr = VTable.GetFinalizationFillPointers(Self, 0, null, out int needed);
            if (!hr)
                return null;

            ClrDataAddress[] pointers = new ClrDataAddress[needed];
            fixed (ClrDataAddress* ptr = pointers)
            {
                hr = VTable.GetFinalizationFillPointers(Self, needed, ptr, out _);
                if (!hr)
                    return null;
            }

            return pointers;
        }

        public ClrDataAddress[]? GetFinalizationFillPointers(ulong heap)
        {
            HResult hr = VTable.GetFinalizationFillPointersSvr(Self, heap, 0, null, out int needed);
            if (!hr)
                return null;

            ClrDataAddress[] pointers = new ClrDataAddress[needed];
            fixed (ClrDataAddress* ptr = pointers)
            {
                hr = VTable.GetFinalizationFillPointersSvr(Self, heap, needed, ptr, out _);
                if (!hr)
                    return null;
            }

            return pointers;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly unsafe struct ISOSDac8VTable
        {
            public readonly delegate* unmanaged[Stdcall]<IntPtr, out int, int> GetNumberGenerations;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int, GenerationData*, out int, int> GetGenerationTable;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int, ClrDataAddress*, out int, int> GetFinalizationFillPointers;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, int, GenerationData*, out int, int> GetGenerationTableSvr;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, int, ClrDataAddress*, out int, int> GetFinalizationFillPointersSvr;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out ClrDataAddress, int> GetAssemblyLoadContext;
        }
    }
}
