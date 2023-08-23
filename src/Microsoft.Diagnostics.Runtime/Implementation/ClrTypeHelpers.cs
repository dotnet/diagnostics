// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime
{
    internal sealed class ClrTypeHelpers : IClrTypeHelpers, IClrFieldHelpers
    {
        private readonly string UnloadedTypeName = "<Unloaded Type>";

        private readonly uint _firstChar = (uint)IntPtr.Size + 4;
        private readonly uint _stringLength = (uint)IntPtr.Size;
        private readonly SOSDac _sos;
        private readonly SOSDac6? _sos6;
        private readonly SOSDac8? _sos8;
        private readonly IClrTypeFactory _typeFactory;
        private readonly IClrMethodHelpers _methodHelpers;

        public CacheOptions CacheOptions { get; }

        public ClrHeap Heap { get; }


        public IDataReader DataReader { get; }

        public ClrTypeHelpers(ClrDataProcess clrDataProcess, SOSDac sos, SOSDac6? sos6, SOSDac8? sos8, IClrTypeFactory typeFactory, ClrHeap heap, CacheOptions cacheOptions)
        {
            _sos = sos;
            _sos6 = sos6;
            _sos8 = sos8;
            _typeFactory = typeFactory;
            CacheOptions = cacheOptions;
            Heap = heap;
            DataReader = heap.Runtime.DataTarget.DataReader;
            _methodHelpers = new ClrMethodHelpers(clrDataProcess, sos, DataReader, cacheOptions);
        }

        public string? ReadString(ulong address, int maxLength)
        {
            if (address == 0)
                return null;


            int length = DataReader.Read<int>(address + _stringLength);
            length = Math.Min(length, maxLength);
            if (length == 0)
                return string.Empty;

            ulong data = address + _firstChar;
            char[] buffer = ArrayPool<char>.Shared.Rent(length);
            try
            {
                Span<char> charSpan = new Span<char>(buffer).Slice(0, length);
                Span<byte> bytes = MemoryMarshal.AsBytes(charSpan);
                int read = DataReader.Read(data, bytes);
                if (read == 0)
                    return null;

                return new string(buffer, 0, read / sizeof(char));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        public ComCallableWrapper? CreateCCWForObject(ulong obj)
        {
            if (!_sos.GetObjectData(obj, out ObjectData data))
                return null;

            if (data.CCW == 0)
                return null;

            if (!_sos.GetCCWData(data.CCW, out CcwData ccwData))
                return null;

            COMInterfacePointerData[]? ptrs = _sos.GetCCWInterfaces(data.CCW, ccwData.InterfaceCount);
            ImmutableArray<ComInterfaceData> interfaces = ptrs != null ? GetComInterfaces(ptrs) : ImmutableArray<ComInterfaceData>.Empty;
            return new(ccwData, interfaces);
        }

        public RuntimeCallableWrapper? CreateRCWForObject(ulong obj)
        {
            if (!_sos.GetObjectData(obj, out ObjectData objData) || objData.RCW == 0)
                return null;

            if (!_sos.GetRCWData(objData.RCW, out RcwData rcw))
                return null;

            COMInterfacePointerData[]? ptrs = _sos.GetRCWInterfaces(objData.RCW, rcw.InterfaceCount);
            ImmutableArray<ComInterfaceData> interfaces = ptrs != null ? GetComInterfaces(ptrs) : ImmutableArray<ComInterfaceData>.Empty;
            return new RuntimeCallableWrapper(objData.RCW, rcw, interfaces);
        }

        public ImmutableArray<ComInterfaceData> GetRCWInterfaces(ulong address, int interfaceCount)
        {
            COMInterfacePointerData[]? ifs = _sos.GetRCWInterfaces(address, interfaceCount);
            if (ifs is null)
                return ImmutableArray<ComInterfaceData>.Empty;

            return GetComInterfaces(ifs);
        }

        private ImmutableArray<ComInterfaceData> GetComInterfaces(COMInterfacePointerData[] ifs)
        {
            ImmutableArray<ComInterfaceData>.Builder result = ImmutableArray.CreateBuilder<ComInterfaceData>(ifs.Length);
            result.Count = result.Capacity;

            for (int i = 0; i < ifs.Length; i++)
                result[i] = new ComInterfaceData(_typeFactory.GetOrCreateType(ifs[i].MethodTable, 0), ifs[i].InterfacePointer);

            return result.MoveOrCopyToImmutable();
        }

        public ClrType? CreateRuntimeType(ClrObject obj)
        {
            if (!obj.IsRuntimeType)
                throw new InvalidOperationException($"Object {obj.Address:x} is of type '{obj.Type?.Name ?? "null"}', expected '{ClrObject.RuntimeTypeName}'.");

            ClrInstanceField? field = obj.Type?.Fields.Where(f => f.Name == "m_handle").FirstOrDefault();
            if (field is null)
                return null;

            ulong mt;
            if (field.ElementType == ClrElementType.NativeInt)
                mt = (ulong)obj.ReadField<IntPtr>("m_handle");
            else
                mt = (ulong)obj.ReadValueTypeField("m_handle").ReadField<IntPtr>("m_ptr");

            return _typeFactory.GetOrCreateType(mt, 0);
        }

        public bool TryGetTypeName(ClrType type, out string? name)
        {
            name = _sos.GetMethodTableName(type.MethodTable);
            if (string.IsNullOrWhiteSpace(name))
                return true;

            if (name == UnloadedTypeName)
            {
                string? nameFromToken = GetNameFromToken(type.Module?.MetadataImport, type.MetadataToken);
                if (nameFromToken is not null)
                    name = nameFromToken;
            }
            else
            {
                name = DACNameParser.Parse(name);
            }

            if (CacheOptions.CacheTypeNames == StringCaching.Intern)
                name = string.Intern(name);

            return CacheOptions.CacheTypeNames != StringCaching.None;
        }

        private static string? GetNameFromToken(MetadataImport? import, int token)
        {
            if (import is not null)
            {
                HResult hr = import.GetTypeDefProperties(token, out string? name, out _, out _);
                if (hr && name is not null)
                {
                    hr = import.GetNestedClassProperties(token, out int enclosingToken);
                    if (hr && enclosingToken != 0 && enclosingToken != token)
                    {
                        string? inner = GetNameFromToken(import, enclosingToken) ?? "<UNKNOWN>";
                        name += $"+{inner}";
                    }

                    return name;
                }
            }

            return null;
        }

        public ulong GetLoaderAllocatorHandle(ulong mt)
        {
            if (_sos6 != null && _sos6.GetMethodTableCollectibleData(mt, out MethodTableCollectibleData data) && data.Collectible != 0)
                return data.LoaderAllocatorObjectHandle;

            return 0;
        }

        public ulong GetAssemblyLoadContextAddress(ulong mt)
        {
            if (_sos8 != null && _sos8.GetAssemblyLoadContext(mt, out ClrDataAddress assemblyLoadContext))
                return assemblyLoadContext;

            return 0;
        }

        public ulong GetObjectDataPointer(ulong objRef)
        {
            if (_sos.GetObjectData(objRef, out ObjectData data))
                return data.ArrayDataPointer;

            return 0;
        }

        public ClrElementType GetObjectElementType(ulong objRef)
        {
            if (_sos.GetObjectData(objRef, out ObjectData data))
                return (ClrElementType)data.ElementType;

            return 0;
        }

        public ImmutableArray<ClrMethod> GetMethodsForType(ClrType type)
        {
            ulong mt = type.MethodTable;
            if (!_sos.GetMethodTableData(mt, out MethodTableData data) || data.NumMethods == 0)
                return ImmutableArray<ClrMethod>.Empty;

            ImmutableArray<ClrMethod>.Builder builder = ImmutableArray.CreateBuilder<ClrMethod>(data.NumMethods);
            for (uint i = 0; i < data.NumMethods; i++)
            {
                ulong slot = _sos.GetMethodTableSlot(mt, i);
                if (_sos.GetCodeHeaderData(slot, out CodeHeaderData chd) && _sos.GetMethodDescData(chd.MethodDesc, 0, out MethodDescData mdd))
                {
                    HotColdRegions regions = new(mdd.NativeCodeAddr, chd.HotRegionSize, chd.ColdRegionStart, chd.ColdRegionSize);
                    builder.Add(new(_methodHelpers, type, chd.MethodDesc, (int)mdd.MDToken, (MethodCompilationType)chd.JITType, regions));
                }
            }

            return builder.MoveOrCopyToImmutable();
        }

        public IEnumerable<ClrField> EnumerateFields(ClrType type)
        {
            int baseFieldCount = 0;
            IEnumerable<ClrField> result = Enumerable.Empty<ClrField>();
            if (type.BaseType is not null)
            {
                result = result.Concat(type.BaseType.Fields);
                baseFieldCount = type.BaseType.Fields.Length;
            }

            return result.Concat(EnumerateFieldsWorker(type, baseFieldCount));
        }

        private IEnumerable<ClrField> EnumerateFieldsWorker(ClrType type, int baseFieldCount)
        {
            if (!_sos.GetFieldInfo(type.MethodTable, out DacInterface.FieldInfo fieldInfo) || fieldInfo.FirstFieldAddress == 0)
                yield break;

            ulong nextField = fieldInfo.FirstFieldAddress;
            for (int i = baseFieldCount; i < fieldInfo.NumInstanceFields + fieldInfo.NumStaticFields; i++)
            {
                if (!_sos.GetFieldData(nextField, out FieldData fieldData))
                    break;

                if (fieldData.IsContextLocal == 0 && fieldData.IsThreadLocal == 0)
                {
                    ClrType? fieldType = _typeFactory.GetOrCreateType(fieldData.TypeMethodTable, 0);
                    if (fieldData.IsStatic != 0)
                        yield return new ClrStaticField(type, fieldType, this, fieldData);
                    else
                        yield return new ClrInstanceField(type, fieldType, this, fieldData);
                }

                nextField = fieldData.NextField;
            }
        }


        public bool ReadProperties(ClrType parentType, int fieldToken, out string? name, out FieldAttributes attributes, ref ClrType? type)
        {
            MetadataImport? import = parentType.Module?.MetadataImport;
            if (import is null || !import.GetFieldProps(fieldToken, out name, out attributes, out IntPtr fieldSig, out int sigLen, out _, out _))
            {
                name = null;
                attributes = default;
                return false;
            }

            if (type is null)
            {
                Utilities.SigParser sigParser = new(fieldSig, sigLen);
                if (sigParser.GetCallingConvInfo(out int sigType) && sigType == Utilities.SigParser.IMAGE_CEE_CS_CALLCONV_FIELD)
                {
                    sigParser.SkipCustomModifiers();
                    type = _typeFactory.GetOrCreateTypeFromSignature(parentType.Module, sigParser, parentType.EnumerateGenericParameters(), Array.Empty<ClrGenericParameter>());
                }
            }

            return true;
        }

        public ulong GetStaticFieldAddress(ClrStaticField field, ulong appDomain)
        {
            if (appDomain == 0)
                return 0;

            ClrType type = field.ContainingType;
            ClrModule? module = type.Module;
            if (module is null)
                return 0;

            bool shared = type.IsShared;

            // TODO: Perf and testing
            if (shared)
            {
                if (!_sos.GetModuleData(module.Address, out ModuleData data))
                    return 0;

                if (!_sos.GetDomainLocalModuleDataFromAppDomain(appDomain, (int)data.ModuleID, out DomainLocalModuleData dlmd))
                    return 0;

                if (!shared && !IsInitialized(dlmd, type.MetadataToken))
                    return 0;

                if (field.ElementType.IsPrimitive())
                    return dlmd.NonGCStaticDataStart + (uint)field.Offset;
                else
                    return dlmd.GCStaticDataStart + (uint)field.Offset;
            }
            else
            {
                if (!_sos.GetDomainLocalModuleDataFromModule(module.Address, out DomainLocalModuleData dlmd))
                    return 0;

                if (field.ElementType.IsPrimitive())
                    return dlmd.NonGCStaticDataStart + (uint)field.Offset;
                else
                    return dlmd.GCStaticDataStart + (uint)field.Offset;
            }
        }

        private bool IsInitialized(in DomainLocalModuleData data, int token)
        {
            ulong flagsAddr = data.ClassData + (uint)(token & ~0x02000000u) - 1;
            if (!DataReader.Read(flagsAddr, out byte flags))
                return false;

            return (flags & 1) != 0;
        }
    }
}