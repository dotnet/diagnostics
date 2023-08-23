// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DbgEng
{
    internal sealed unsafe class DebugSymbols : CallableCOMWrapper
    {
        internal static readonly Guid IID_IDebugSymbols3 = new("f02fbecc-50ac-4f36-9ad9-c975e8f32ff8");

        public DebugSymbols(RefCountedFreeLibrary library, IntPtr pUnk, DebugSystemObjects sys)
            : base(library, IID_IDebugSymbols3, pUnk)
        {
            _sys = sys;
            SuppressRelease();
        }

        private ref readonly IDebugSymbols3VTable VTable => ref Unsafe.AsRef<IDebugSymbols3VTable>(_vtable);

        public string? GetModuleNameStringWide(DebugModuleName which, int index, ulong imageBase)
        {
            using IDisposable holder = _sys.Enter();
            HResult hr = VTable.GetModuleNameStringWide(Self, which, index, imageBase, null, 0, out int needed);
            if (!hr)
                return null;

            string nameResult = new('\0', needed - 1);
            fixed (char* nameResultPtr = nameResult)
            {
                hr = VTable.GetModuleNameStringWide(Self, which, index, imageBase, nameResultPtr, needed, out _);
                if (hr)
                    return nameResult;
            }

            return null;
        }

        public int GetNumberModules()
        {
            using IDisposable holder = _sys.Enter();
            HResult hr = VTable.GetNumberModules(Self, out int count, out _);
            return hr ? count : 0;
        }

        public ulong GetModuleByIndex(int i)
        {
            using IDisposable holder = _sys.Enter();
            HResult hr = VTable.GetModuleByIndex(Self, i, out ulong imageBase);
            return hr ? imageBase : 0;
        }

        public HResult GetModuleParameters(ulong[] bases, out DEBUG_MODULE_PARAMETERS[] parameters)
        {
            parameters = new DEBUG_MODULE_PARAMETERS[bases.Length];

            fixed (ulong* pBases = bases)
            fixed (DEBUG_MODULE_PARAMETERS* pParams = parameters)
            {
                using IDisposable holder = _sys.Enter();
                HResult hr = VTable.GetModuleParameters(Self, bases.Length, pBases, 0, pParams);
                return hr;
            }
        }

        public Version GetModuleVersionInformation(int index, ulong imgBase)
        {
            byte* item = stackalloc byte[3] { (byte)'\\', (byte)'\\', 0 };

            using IDisposable holder = _sys.Enter();

            byte[] buffer = ArrayPool<byte>.Shared.Rent(256);
            try
            {
                HResult hr;
                fixed (byte* pBuffer = buffer)
                    hr = VTable.GetModuleVersionInformation(Self, index, imgBase, item, pBuffer, buffer.Length, out _);

                if (!hr)
                    return new Version();

                int minor = Unsafe.As<byte, ushort>(ref buffer[8]);
                int major = Unsafe.As<byte, ushort>(ref buffer[10]);
                int patch = Unsafe.As<byte, ushort>(ref buffer[12]);
                int revision = Unsafe.As<byte, ushort>(ref buffer[14]);

                return new Version(major, minor, revision, patch);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public HResult GetModuleByOffset(ulong address, int index, out int outIndex, out ulong imgBase)
        {
            using IDisposable holder = _sys.Enter();
            return VTable.GetModuleByOffset(Self, address, index, out outIndex, out imgBase);
        }
        private readonly DebugSystemObjects _sys;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct IDebugSymbols3VTable
    {
        public readonly IntPtr GetSymbolOptions;
        public readonly IntPtr AddSymbolOptions;
        public readonly IntPtr RemoveSymbolOptions;
        public readonly IntPtr SetSymbolOptions;
        public readonly IntPtr GetNameByOffset;
        public readonly IntPtr GetOffsetByName;
        public readonly IntPtr GetNearNameByOffset;
        public readonly IntPtr GetLineByOffset;
        public readonly IntPtr GetOffsetByLine;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out int, out int, int> GetNumberModules;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, out ulong, int> GetModuleByIndex;
        public readonly IntPtr GetModuleByModuleName;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, int, out int, out ulong, int> GetModuleByOffset;
        public readonly IntPtr GetModuleNames;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, ulong*, int, DEBUG_MODULE_PARAMETERS*, int> GetModuleParameters;
        public readonly IntPtr GetSymbolModule;
        public readonly IntPtr GetTypeName;
        public readonly IntPtr GetTypeId;
        public readonly IntPtr GetTypeSize;
        public readonly IntPtr GetFieldOffset;
        public readonly IntPtr GetSymbolTypeId;
        public readonly IntPtr GetOffsetTypeId;
        public readonly IntPtr ReadTypedDataVirtual;
        public readonly IntPtr WriteTypedDataVirtual;
        public readonly IntPtr OutputTypedDataVirtual;
        public readonly IntPtr ReadTypedDataPhysical;
        public readonly IntPtr WriteTypedDataPhysical;
        public readonly IntPtr OutputTypedDataPhysical;
        public readonly IntPtr GetScope;
        public readonly IntPtr SetScope;
        public readonly IntPtr ResetScope;
        public readonly IntPtr GetScopeSymbolGroup;
        public readonly IntPtr CreateSymbolGroup;
        public readonly IntPtr StartSymbolMatch;
        public readonly IntPtr GetNextSymbolMatch;
        public readonly IntPtr EndSymbolMatch;
        public readonly IntPtr Reload;
        public readonly IntPtr GetSymbolPath;
        public readonly IntPtr SetSymbolPath;
        public readonly IntPtr AppendSymbolPath;
        public readonly IntPtr GetImagePath;
        public readonly IntPtr SetImagePath;
        public readonly IntPtr AppendImagePath;
        public readonly IntPtr GetSourcePath;
        public readonly IntPtr GetSourcePathElement;
        public readonly IntPtr SetSourcePath;
        public readonly IntPtr AppendSourcePath;
        public readonly IntPtr FindSourceFile;
        public readonly IntPtr GetSourceFileLineOffsets;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, ulong, byte*, byte*, int, out int, int> GetModuleVersionInformation;
        public readonly IntPtr GetModuleNameString;
        public readonly IntPtr GetConstantName;
        public readonly IntPtr GetFieldName;
        public readonly IntPtr GetTypeOptions;
        public readonly IntPtr AddTypeOptions;
        public readonly IntPtr RemoveTypeOptions;
        public readonly IntPtr SetTypeOptions;
        public readonly IntPtr GetNameByOffsetWide;
        public readonly IntPtr GetOffsetByNameWide;
        public readonly IntPtr GetNearNameByOffsetWide;
        public readonly IntPtr GetLineByOffsetWide;
        public readonly IntPtr GetOffsetByLineWide;
        public readonly IntPtr GetModuleByModuleNameWide;
        public readonly IntPtr GetSymbolModuleWide;
        public readonly IntPtr GetTypeNameWide;
        public readonly IntPtr GetTypeIdWide;
        public readonly IntPtr GetFieldOffsetWide;
        public readonly IntPtr GetSymbolTypeIdWide;
        public readonly IntPtr GetScopeSymbolGroup2;
        public readonly IntPtr CreateSymbolGroup2;
        public readonly IntPtr StartSymbolMatchWide;
        public readonly IntPtr GetNextSymbolMatchWide;
        public readonly IntPtr ReloadWide;
        public readonly IntPtr GetSymbolPathWide;
        public readonly IntPtr SetSymbolPathWide;
        public readonly IntPtr AppendSymbolPathWide;
        public readonly IntPtr GetImagePathWide;
        public readonly IntPtr SetImagePathWide;
        public readonly IntPtr AppendImagePathWide;
        public readonly IntPtr GetSourcePathWide;
        public readonly IntPtr GetSourcePathElementWide;
        public readonly IntPtr SetSourcePathWide;
        public readonly IntPtr AppendSourcePathWide;
        public readonly IntPtr FindSourceFileWide;
        public readonly IntPtr GetSourceFileLineOffsetsWide;
        public readonly IntPtr GetModuleVersionInformationWide;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, DebugModuleName, int, ulong, char*, int, out int, int> GetModuleNameStringWide;
        public readonly IntPtr GetConstantNameWide;
        public readonly IntPtr GetFieldNameWide;
        public readonly IntPtr IsManagedModule;
        public readonly IntPtr GetModuleByModuleName2;
        public readonly IntPtr GetModuleByModuleName2Wide;
        public readonly IntPtr GetModuleByOffset2;
        public readonly IntPtr AddSyntheticModule;
        public readonly IntPtr AddSyntheticModuleWide;
        public readonly IntPtr RemoveSyntheticModule;
        public readonly IntPtr GetCurrentScopeFrameIndex;
        public readonly IntPtr SetScopeFrameByIndex;
        public readonly IntPtr SetScopeFromJitDebugInfo;
        public readonly IntPtr SetScopeFromStoredEvent;
        public readonly IntPtr OutputSymbolByOffset;
        public readonly IntPtr GetFunctionEntryByOffset;
        public readonly IntPtr GetFieldTypeAndOffset;
        public readonly IntPtr GetFieldTypeAndOffsetWide;
        public readonly IntPtr AddSyntheticSymbol;
        public readonly IntPtr AddSyntheticSymbolWide;
        public readonly IntPtr RemoveSyntheticSymbol;
        public readonly IntPtr GetSymbolEntriesByOffset;
        public readonly IntPtr GetSymbolEntriesByName;
        public readonly IntPtr GetSymbolEntriesByNameWide;
        public readonly IntPtr GetSymbolEntryByToken;
        public readonly IntPtr GetSymbolEntryInformation;
        public readonly IntPtr GetSymbolEntryString;
        public readonly IntPtr GetSymbolEntryStringWide;
        public readonly IntPtr GetSymbolEntryOffsetRegions;
        public readonly IntPtr GetSymbolEntryBySymbolEntry;
        public readonly IntPtr GetSourceEntriesByOffset;
        public readonly IntPtr GetSourceEntriesByLine;
        public readonly IntPtr GetSourceEntriesByLineWide;
        public readonly IntPtr GetSourceEntryString;
        public readonly IntPtr GetSourceEntryStringWide;
        public readonly IntPtr GetSourceEntryOffsetRegions;
        public readonly IntPtr GetSourceEntryBySourceEntry;
    }
}