// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DataContractReader.Legacy;
using static Microsoft.Diagnostics.ExtensionCommands.DacCommandHelpers;
using static SOS.Hosting.Interop.ClrDataAddressHelpers;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    internal sealed class DacObjectRenderer(ISOSDacInterface dac, IConsoleService console, IMemoryService memory, IModuleService modules, OSPlatform targetOS, bool dml)
    {
        private const uint MaxStringSize = 0x200000;
        private const byte ClassInitialized = 1;
        private const byte ClassInitializationFailed = 2;

        private static readonly string[] s_elementTypeNames =
        [
            "", "Void", "Boolean", "Char", "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64",
            "Single", "Double", "String", "PTR", "BYREF", "VALUETYPE", "CLASS", "VAR", "ARRAY", "", "TypedReference",
            "", "IntPtr", "UIntPtr", "", "FNPTR", "Object", "SZARRAY", "MVAR",
        ];

        internal unsafe void WriteObject(ulong address, bool printFields, HostType hostType)
        {
            ulong dacAddress = ToClrDataAddress(address, memory.PointerSize);
            DacpObjectData data = ReadObject(address, hostType, warnOnInvalidGc: true, out string name);
            if (data.ObjectType == DacpObjectType.OBJ_FREE)
            {
                WriteFreeObject(data.Size);
                return;
            }

            console.WriteLine($"Name:        {name}");
            WriteAddress("MethodTable: ", data.MethodTable, "DumpMT");

            DacpMethodTableData methodTable = default;
            if (dac.GetMethodTableData(data.MethodTable, &methodTable) != 0)
            {
                throw new DiagnosticsException("Invalid MethodTable address");
            }

            WriteSummary(address, dacAddress, data, methodTable, name);
            if (printFields)
            {
                WriteFields(address, data.MethodTable, methodTable);
            }

            WriteThinLock(address);
        }

        internal unsafe DacpObjectData ReadObject(ulong address, HostType hostType, bool warnOnInvalidGc, out string name)
        {
            DacpObjectData data = default;
            int hr = dac.GetObjectData(ToClrDataAddress(address, memory.PointerSize), &data);
            name = hr >= 0 && data.ObjectType != DacpObjectType.OBJ_FREE ? GetObjectTypeName(dac, address, memory.PointerSize) : null;
            if (hr < 0 || (data.ObjectType != DacpObjectType.OBJ_FREE && name != "<Unloaded Type>" && !HasValidMethodTables(data.MethodTable)))
            {
                console.WriteLine("<Note: this object has an invalid CLASS field>");
                if (warnOnInvalidGc)
                {
                    WriteInvalidGcWarning(hostType);
                }
            }
            if (hr != 0)
            {
                throw new DiagnosticsException(hostType == HostType.DotnetDump ? "Invalid object\n" : "Invalid object");
            }
            return data;
        }

        internal void WriteFreeObject(ulong size)
        {
            console.WriteLine("Free Object");
            WriteSize(size);
        }

        private unsafe bool HasValidMethodTables(ulong methodTable)
        {
            // Native VerifyMemberFields never populates its field descriptor list.
            // Its effective check walks the method table hierarchy without inspecting members.
            HashSet<ulong> visited = new();
            do
            {
                console.CancellationToken.ThrowIfCancellationRequested();
                DacpMethodTableData data = default;
                if (!visited.Add(methodTable) || dac.GetMethodTableData(methodTable, &data) < 0)
                {
                    return false;
                }
                methodTable = data.parentMethodTable;
            }
            while (methodTable != 0);

            return true;
        }

        private unsafe void WriteInvalidGcWarning(HostType hostType)
        {
            DacpGcHeapData heap = default;
            int hr = dac.GetGCHeapData(&heap);
            if (hr != 0)
            {
                console.WriteLine($"GetGcStructuresValid: request heap data FAILED {hr:x8}");
            }
            else if (heap.bGcStructuresValid != 0)
            {
                return;
            }

            string prefix = hostType == HostType.DbgEng ? "!" : "";
            console.WriteLine("The garbage collector data structures are not in a valid state for traversal.");
            console.WriteLine("It is either in the \"plan phase,\" where objects are being moved around, or");
            console.WriteLine("we are at the initialization or shutdown of the gc heap. Commands related to");
            console.WriteLine("displaying, finding or traversing objects as well as gc heap segments may not");
            console.WriteLine($"work properly. {prefix}dumpheap and {prefix}verifyheap may incorrectly complain of heap");
            console.WriteLine("consistency errors.");
        }

        private unsafe void WriteSummary(ulong address, ulong dacAddress, DacpObjectData data, DacpMethodTableData methodTable, string name)
        {
            if (data.RCW != 0)
            {
                WriteAddress("RCW:         ", data.RCW, "DumpRCW");
            }
            if (data.CCW != 0)
            {
                WriteAddress("CCW:         ", data.CCW, "DumpCCW");
            }

            WriteComWrappers(dacAddress);
            WriteTrackedType(dacAddress);
            WriteSize(data.Size);
            if (name == "System.RuntimeType")
            {
                WriteRuntimeType(address, data.MethodTable);
            }
            else if (name == "System.RuntimeType+RuntimeTypeCache" && TryReadInstancePointer(address, data.MethodTable, "m_runtimeType", out ulong runtimeType))
            {
                DacpObjectData runtimeTypeData = default;
                if (dac.GetObjectData(ToClrDataAddress(runtimeType, memory.PointerSize), &runtimeTypeData) != 0)
                {
                    throw new DiagnosticsException("Error when reading RuntimeType field");
                }
                WriteRuntimeType(runtimeType, runtimeTypeData.MethodTable);
            }
            if (data.ObjectType == DacpObjectType.OBJ_ARRAY)
            {
                WriteArray(address, data);
            }
            else
            {
                DacpModuleData module = default;
                string path = dac.GetModuleData(methodTable.module, &module) == 0 ? GetModuleName(dac, module, modules, targetOS, memory.PointerSize) : null;
                console.WriteLine($"File:        {path ?? "Unknown Module"}");
            }

            if (data.ObjectType == DacpObjectType.OBJ_STRING)
            {
                WriteString(address, dacAddress, data.Size);
            }
        }

        private unsafe void WriteComWrappers(ulong address)
        {
            ClrDataAddress rcw = 0;
            uint count = 0;
            if (dac is not ISOSDacInterface10 wrappers ||
                wrappers.GetObjectComWrappersData(address, &rcw, 0, null, &count) < 0 ||
                (rcw == 0 && count == 0))
            {
                return;
            }

            ClrDataAddress[] ccws;
            try
            {
                ccws = count == 0 ? [] : new ClrDataAddress[count];
            }
            catch (OutOfMemoryException)
            {
                console.WriteLine("SOS Error: Out of memory");
                return;
            }

            if (wrappers.GetObjectComWrappersData(address, &rcw, count, ccws, null) < 0)
            {
                console.WriteLine("Failed to get ComWrappers RCW/CCW data for the object");
                return;
            }

            if (rcw != 0)
            {
                WriteAddress("ComWrappers RCW: ", rcw, "DumpRCW");
            }
            if (ccws.Length != 0)
            {
                console.WriteLine("ComWrappers CCWs:");
                foreach (ulong ccw in ccws)
                {
                    console.CancellationToken.ThrowIfCancellationRequested();
                    WriteAddress("             ", ccw, "DumpCCW");
                }
            }
        }

        private unsafe void WriteTrackedType(ulong address)
        {
            Interop.BOOL isTrackedType = Interop.BOOL.FALSE;
            Interop.BOOL hasTaggedMemory = Interop.BOOL.FALSE;
            if (dac is not ISOSDacInterface11 tracking ||
                tracking.IsTrackedType(address, &isTrackedType, &hasTaggedMemory) < 0)
            {
                return;
            }

            if (isTrackedType != Interop.BOOL.FALSE)
            {
                console.WriteLine("Tracked Type: true");
            }
            if (hasTaggedMemory != Interop.BOOL.FALSE)
            {
                ClrDataAddress taggedMemory = 0;
                nuint size = 0;
                // Native SOS prints the returned address and size even if this lookup fails.
                _ = tracking.GetTaggedMemory(address, &taggedMemory, &size);
                string pointer = FormatDacAddress(taggedMemory, memory.PointerSize);
                console.Write("Tagged Memory: ");
                console.WriteDmlExec(pointer, dml ? $"dps {pointer} L{size / (uint)memory.PointerSize}" : null, forceDml: dml);
                console.WriteLine($" ({size}(0x{size:x}) bytes)");
            }
        }

        private unsafe void WriteFields(ulong address, ulong methodTable, DacpMethodTableData type)
        {
            DacpMethodTableFieldData fields = default;
            if (dac.GetMethodTableFieldData(methodTable, &fields) != 0)
            {
                throw new DiagnosticsException("Unable to display fields");
            }
            console.WriteLine("Fields:");
            if (fields.wNumInstanceFields == 0 && fields.wNumStaticFields == 0)
            {
                console.WriteLine("None");
                return;
            }

            int width = memory.PointerSize * 2;
            console.WriteLine($"{"MT".PadLeft(width)} {"Field",8} {"Offset",8} {"Type",20} {"VT",4} {"Attr",8} {"Value".PadLeft(width)} Name");
            Stack<(ulong MethodTable, DacpMethodTableData Type, DacpMethodTableFieldData Fields)> hierarchy = new();
            HashSet<ulong> visited = new();
            while (visited.Add(methodTable))
            {
                console.CancellationToken.ThrowIfCancellationRequested();
                hierarchy.Push((methodTable, type, fields));
                methodTable = type.parentMethodTable;
                if (methodTable == 0)
                {
                    break;
                }
                if (dac.GetMethodTableData(methodTable, &type) != 0 || dac.GetMethodTableFieldData(methodTable, &fields) != 0)
                {
                    throw new DiagnosticsException("Unable to read parent method table fields");
                }
            }
            if (methodTable != 0)
            {
                throw new DiagnosticsException("Cycle in parent method tables");
            }

            int instanceCount = 0;
            while (hierarchy.TryPop(out (ulong MethodTable, DacpMethodTableData Type, DacpMethodTableFieldData Fields) entry))
            {
                int staticCount = 0;
                ulong fieldAddress = entry.Fields.FirstField;
                if (instanceCount >= entry.Fields.wNumInstanceFields && entry.Fields.wNumStaticFields == 0)
                {
                    continue;
                }

                using MetadataReaderProvider metadata = OpenMetadata(entry.Type.module);
                MetadataReader reader = metadata?.GetMetadataReader();
                while (instanceCount < entry.Fields.wNumInstanceFields || staticCount < entry.Fields.wNumStaticFields)
                {
                    console.CancellationToken.ThrowIfCancellationRequested();
                    DacpFieldDescData field = default;
                    if (fieldAddress == 0 || dac.GetFieldDescData(fieldAddress, &field) != 0)
                    {
                        throw new DiagnosticsException("Unable to display fields");
                    }
                    fieldAddress = field.NextField;
                    uint token = field.mb | 0x04000000;
                    int row = (int)(token & 0x00ffffff);
                    string name = reader != null && row > 0 && row <= reader.FieldDefinitions.Count
                        ? reader.GetString(reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(row)).Name)
                        : "<unknown field>";
                    if (field.bIsStatic != 0)
                    {
                        staticCount++;
                        bool special = field.bIsThreadLocal != 0 || field.bIsContextLocal != 0 || entry.Type.bIsShared != 0;
                        uint offset = special ? field.dwOffset : field.dwOffset + (uint)memory.PointerSize;
                        string kind = field.bIsThreadLocal != 0 ? "TLstatic" : field.bIsContextLocal != 0 ? "CLstatic" : "static";
                        bool shared = entry.Type.bIsShared != 0;
                        WriteFieldPrefix(field, token, offset, shared ? "shared" : kind, reader);
                        if (special)
                        {
                            console.WriteLine($"{(shared ? kind.PadLeft(width) : "")} {name}");
                            if (field.bIsThreadLocal != 0)
                            {
                                WriteThreadStatic(entry.MethodTable, entry.Type, field);
                            }
                            else if (field.bIsContextLocal != 0)
                            {
                                console.WriteLine("\nDisplay of context static variables is not implemented");
                            }
                            else
                            {
                                WriteSharedStatic(entry.Type, field);
                            }
                        }
                        else if (TryGetStaticAddress(entry.MethodTable, entry.Type, field, out ulong valueAddress))
                        {
                            WriteFieldValue(field, valueAddress);
                            console.WriteLine($" {name}");
                        }
                        else
                        {
                            console.WriteLine(" <no information>");
                        }
                    }
                    else
                    {
                        instanceCount++;
                        uint offset = field.dwOffset + (uint)memory.PointerSize;
                        WriteFieldPrefix(field, token, offset, "instance", reader);
                        WriteFieldValue(field, address + offset);
                        console.WriteLine($" {name}");
                    }
                }
            }
        }

        private unsafe void WriteThreadStatic(ulong methodTable, DacpMethodTableData type, DacpFieldDescData field)
        {
            DacpModuleData module = default;
            if (dac.GetModuleData(type.module, &module) != 0)
            {
                return;
            }

            DacpThreadStoreData store = default;
            _ = dac.GetThreadStoreData(&store);
            console.WriteLine("    Thread static values (Thread:Value)");
            ISOSDacInterface14 statics = dac as ISOSDacInterface14;
            HashSet<ulong> visited = new();
            for (ulong current = store.firstThread; current != 0;)
            {
                console.CancellationToken.ThrowIfCancellationRequested();
                DacpThreadData thread = default;
                if (!visited.Add(current) || dac.GetThreadData(current, &thread) != 0)
                {
                    console.WriteLine($"  error getting thread {FormatDacAddress(current, memory.PointerSize, upperCase: targetOS == OSPlatform.Windows)}, aborting this field");
                    return;
                }

                if (thread.osThreadId != 0 && TryGetThreadStaticAddress(statics, current, thread.domain, methodTable, type, module, field, out ulong address))
                {
                    console.Write($"        {thread.osThreadId:x}:");
                    WriteFieldValue(field, address, align: false);
                    console.WriteLine();
                }
                current = thread.nextThread;
            }
            console.WriteLine();
        }

        private unsafe bool TryGetThreadStaticAddress(ISOSDacInterface14 statics, ulong thread, ulong domain, ulong methodTable, DacpMethodTableData type, DacpModuleData module, DacpFieldDescData field, out ulong address)
        {
            const byte ThreadStaticAllocated = 4;
            address = 0;
            bool gcStatic = (uint)field.Type is 0x11 or 0x12;
            ClrDataAddress baseAddress = 0;
            if (statics != null)
            {
                if (statics.GetThreadStaticBaseAddress(methodTable, thread, gcStatic ? null : &baseAddress, gcStatic ? &baseAddress : null) < 0)
                {
                    console.Write("GetThreadStaticBaseAddress failed");
                    return false;
                }
                MethodTableInitializationFlags flags = 0;
                if (statics.GetMethodTableInitializationFlags(methodTable, &flags) < 0)
                {
                    console.Write("GetMethodTableInitializationFlags failed");
                    flags = 0;
                }
                if (baseAddress == 0 && ((int)flags & ThreadStaticAllocated) == 0)
                {
                    return false;
                }
            }
            else
            {
                DacpLocalModuleData domainModule = default;
                bool hasDomainModule = type.bIsShared != 0
                    ? TryGetDomainModule(domain, module.dwModuleID, &domainModule)
                    : dac.GetDomainLocalModuleDataFromModule(type.module, &domainModule) == 0;
                DacpLocalModuleData threadModule = default;
                if (!hasDomainModule || dac.GetThreadLocalModuleData(thread, unchecked((uint)module.dwModuleIndex), &threadModule) != 0)
                {
                    return false;
                }
                if (type.bIsDynamic != 0)
                {
                    console.Write("dynamic thread statics NYI");
                    return false;
                }
                uint rid = type.cl & 0x00ffffff;
                if ((ReadClassFlags(threadModule.ClassData, rid) & ThreadStaticAllocated) == 0 ||
                    (ReadClassFlags(domainModule.ClassData, rid) & ClassInitialized) == 0)
                {
                    return false;
                }
                baseAddress = gcStatic ? threadModule.GcStaticDataStart : threadModule.NonGcStaticDataStart;
            }

            address = ToTargetAddress(baseAddress, memory.PointerSize) + field.dwOffset;
            return true;
        }

        private unsafe bool TryGetDomainModule(ulong domain, ulong moduleId, DacpLocalModuleData* data)
            => dac.GetDomainLocalModuleDataFromAppDomain(domain, unchecked((int)moduleId), data) == 0 ||
               dac.GetDomainLocalModuleData(moduleId, data) == 0;

        private byte ReadClassFlags(ulong classData, uint rid)
        {
            Span<byte> value = stackalloc byte[1];
            return classData != 0 && rid != 0 &&
                memory.ReadMemory(ToTargetAddress(classData, memory.PointerSize) + rid - 1, value, out int bytesRead) &&
                bytesRead == 1 ? value[0] : (byte)0;
        }

        private unsafe void WriteSharedStatic(DacpMethodTableData type, DacpFieldDescData field)
        {
            DacpModuleData module = default;
            if (dac.GetModuleData(type.module, &module) != 0)
            {
                return;
            }

            DacpAppDomainStoreData store = default;
            if (dac.GetAppDomainStoreData(&store) != 0 || store.DomainCount < 0)
            {
                console.WriteLine("Unable to get AppDomain information");
                return;
            }

            ClrDataAddress[] domains = new ClrDataAddress[store.DomainCount];
            if (dac.GetAppDomainList((uint)domains.Length, domains, null) != 0)
            {
                console.WriteLine("Unable to get array of AppDomains");
                return;
            }

            console.Write(targetOS == OSPlatform.Windows && memory.PointerSize == 8 ? new string(' ', 33) : "    ");
            console.Write(">> Domain:Value ");
            foreach (ulong address in domains)
            {
                console.CancellationToken.ThrowIfCancellationRequested();
                DacpAppDomainData domain = default;
                if (dac.GetAppDomainData(address, &domain) != 0)
                {
                    console.WriteLine($"Unable to get AppDomain {unchecked((uint)address):x}");
                    return;
                }

                DacpLocalModuleData domainModule = default;
                ClrDataAddress baseAddress = 0;
                byte flags = 0;
                if (TryGetDomainModule(domain.AppDomainPtr, module.dwModuleID, &domainModule))
                {
                    baseAddress = (uint)field.Type is 0x11 or 0x12 ? domainModule.GcStaticDataStart : domainModule.NonGcStaticDataStart;
                    if (baseAddress != 0)
                    {
                        if (type.bIsDynamic != 0)
                        {
                            console.Write("dynamic statics NYI");
                        }
                        else
                        {
                            flags = ReadClassFlags(domainModule.ClassData, type.cl & 0x00ffffff);
                        }
                    }
                }

                console.Write(" ");
                WriteDacAddressLink(console, (flags & ClassInitialized) == 0 || (flags & ClassInitializationFailed) != 0 ? address : domain.AppDomainPtr,
                    memory.PointerSize, "DumpDomain", dml, forceDml: dml);
                if ((flags & ClassInitialized) == 0)
                {
                    console.Write(":NotInit ");
                }
                else if ((flags & ClassInitializationFailed) != 0)
                {
                    console.Write(":FailInit");
                }
                else
                {
                    console.Write(":");
                    WriteFieldValue(field, ToTargetAddress(baseAddress, memory.PointerSize) + field.dwOffset, align: false);
                }
            }
            console.WriteLine(" <<");
        }

        private unsafe bool TryGetStaticAddress(ulong methodTable, DacpMethodTableData type, DacpFieldDescData field, out ulong address)
        {
            address = 0;
            bool gcStatic = (uint)field.Type is 0x11 or 0x12;
            ClrDataAddress baseAddress = 0;
            if (dac is ISOSDacInterface14 statics)
            {
                if (statics.GetStaticBaseAddress(methodTable, gcStatic ? null : &baseAddress, gcStatic ? &baseAddress : null) < 0)
                {
                    console.Write("GetStaticBaseAddress failed");
                    return false;
                }
            }
            else
            {
                DacpLocalModuleData module = default;
                if (dac.GetDomainLocalModuleDataFromModule(type.module, &module) < 0)
                {
                    return false;
                }
                baseAddress = gcStatic ? module.GcStaticDataStart : module.NonGcStaticDataStart;
                if (baseAddress != 0 && type.bIsDynamic != 0)
                {
                    console.Write("dynamic statics NYI");
                    return false;
                }
            }
            if (baseAddress != 0)
            {
                address = ToTargetAddress(baseAddress, memory.PointerSize) + field.dwOffset;
            }
            return true;
        }

        private unsafe void WriteRuntimeType(ulong address, ulong methodTable)
        {
            if (!TryReadInstancePointer(address, methodTable, "m_handle", out ulong handle))
            {
                return;
            }
            const ulong TypeDescBit = 2;
            if ((handle & TypeDescBit) != 0)
            {
                console.WriteLine($"TypeDesc:    {FormatDacAddress(handle & ~TypeDescBit, memory.PointerSize, upperCase: targetOS == OSPlatform.Windows)}");
                return;
            }
            Span<char> name = stackalloc char[2048];
            fixed (char* buffer = name)
            {
                string typeName = dac.GetMethodTableName(ToClrDataAddress(handle, memory.PointerSize), (uint)name.Length, buffer, null) >= 0
                    ? ReadNullTerminatedString(name) : "<error>";
                console.WriteLine($"Type Name:   {typeName}");
            }
            WriteAddress("Type MT:     ", handle, "DumpMT");
        }

        private unsafe bool TryReadInstancePointer(ulong address, ulong methodTable, string name, out ulong value)
        {
            value = 0;
            DacpMethodTableData type = default;
            DacpMethodTableFieldData fields = default;
            if (dac.GetMethodTableData(methodTable, &type) != 0 || dac.GetMethodTableFieldData(methodTable, &fields) != 0)
            {
                return false;
            }
            DacpMethodTableFieldData parent = default;
            if (type.parentMethodTable != 0 && dac.GetMethodTableFieldData(type.parentMethodTable, &parent) != 0)
            {
                return false;
            }
            using MetadataReaderProvider metadata = OpenMetadata(type.module);
            MetadataReader reader = metadata?.GetMetadataReader();
            if (reader == null)
            {
                return false;
            }
            ulong fieldAddress = fields.FirstField;
            int count = fields.wNumInstanceFields - parent.wNumInstanceFields + fields.wNumStaticFields;
            for (int i = 0; i < count && fieldAddress != 0; i++)
            {
                DacpFieldDescData field = default;
                if (dac.GetFieldDescData(fieldAddress, &field) != 0)
                {
                    break;
                }
                int row = (int)(field.mb & 0x00ffffff);
                if (field.bIsStatic == 0 && row > 0 && row <= reader.FieldDefinitions.Count &&
                    reader.StringComparer.Equals(reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(row)).Name, name))
                {
                    return memory.ReadPointer(address + (uint)memory.PointerSize + field.dwOffset, out value);
                }
                fieldAddress = field.NextField;
            }
            return false;
        }

        private unsafe MetadataReaderProvider OpenMetadata(ulong moduleAddress)
        {
            const int MaxMetadataSize = 64 * 1024 * 1024;
            DacpModuleData module = default;
            if (dac.GetModuleData(moduleAddress, &module) != 0 || module.metadataStart == 0 || module.metadataSize is 0 or > MaxMetadataSize)
            {
                return null;
            }

            byte[] metadata = new byte[(int)module.metadataSize];
            return memory.ReadMemory(ToTargetAddress(module.metadataStart, memory.PointerSize), metadata, out int bytesRead) && bytesRead == metadata.Length
                ? MetadataReaderProvider.FromMetadataImage(ImmutableCollectionsMarshal.AsImmutableArray(metadata))
                : null;
        }

        private unsafe void WriteFieldPrefix(DacpFieldDescData field, uint token, uint offset, string kind, MetadataReader reader)
        {
            uint signatureType = (uint)field.sigType;
            string typeName = signatureType < s_elementTypeNames.Length ? s_elementTypeNames[signatureType] : "";
            if (signatureType is >= 1 and <= 14 or 22 or 24 or 25 or 28)
            {
                typeName = $"System.{typeName}";
            }
            if ((uint)field.Type is 0x11 or 0x12 && field.MTOfType != 0)
            {
                Span<char> buffer = stackalloc char[2048];
                fixed (char* name = buffer)
                {
                    if (dac.GetMethodTableName(field.MTOfType, (uint)buffer.Length, name, null) >= 0)
                    {
                        typeName = ReadNullTerminatedString(buffer);
                    }
                }
            }
            else if ((uint)field.Type == 0x12 && reader != null)
            {
                int row = (int)(field.TokenOfType & 0x00ffffff);
                if (row > 0 && row <= reader.TypeDefinitions.Count)
                {
                    TypeDefinition definition = reader.GetTypeDefinition(MetadataTokens.TypeDefinitionHandle(row));
                    string ns = reader.GetString(definition.Namespace);
                    typeName = reader.GetString(definition.Name);
                    if (ns.Length != 0)
                    {
                        typeName = $"{ns}.{typeName}";
                    }
                }
            }
            if (typeName.Length > 20)
            {
                typeName = "..." + typeName[^17..];
            }

            WriteDacAddressLink(console, field.MTOfType, memory.PointerSize, "DumpMT", dml, forceDml: dml);
            string valueType = (uint)field.Type is >= 2 and <= 13 or 0x11 or 0x18 or 0x19 ? "Yes" : "No";
            console.Write($" {token:x8} {offset,8:x} {typeName,20} {valueType,4} {kind,8} ");
        }

        private void WriteFieldValue(DacpFieldDescData field, ulong address, bool align = true)
        {
            int width = memory.PointerSize * 2;
            if (address == 0)
            {
                console.Write(new string(' ', width));
                return;
            }
            if ((uint)field.Type == 0x11 && field.bIsStatic == 0)
            {
                string value = FormatDacAddress(address, memory.PointerSize);
                string methodTable = FormatDacAddress(field.MTOfType, memory.PointerSize);
                console.WriteDmlExec(value, dml ? $"!DumpVC /d {methodTable} {value}" : null, forceDml: dml);
                return;
            }

            int size = (uint)field.Type switch
            {
                >= 0x02 and <= 0x0d => (uint)field.Type switch
                {
                    0x02 or 0x04 or 0x05 => 1,
                    0x03 or 0x06 or 0x07 => 2,
                    0x08 or 0x09 or 0x0c => 4,
                    _ => 8,
                },
                _ => memory.PointerSize,
            };
            Span<byte> valueBytes = stackalloc byte[size];
            if (!memory.ReadMemory(address, valueBytes, out int bytesRead) || bytesRead != size)
            {
                console.Write(align ? "<unreadable>".PadLeft(width) : "<unreadable>");
                return;
            }

            string text = (uint)field.Type switch
            {
                0x02 or 0x05 => valueBytes[0].ToString(CultureInfo.InvariantCulture),
                0x03 => MemoryMarshal.Read<ushort>(valueBytes).ToString("x", CultureInfo.InvariantCulture),
                0x04 => unchecked((sbyte)valueBytes[0]).ToString(CultureInfo.InvariantCulture),
                0x06 => MemoryMarshal.Read<short>(valueBytes).ToString(CultureInfo.InvariantCulture),
                0x07 => MemoryMarshal.Read<ushort>(valueBytes).ToString(CultureInfo.InvariantCulture),
                0x08 => MemoryMarshal.Read<int>(valueBytes).ToString(CultureInfo.InvariantCulture),
                0x09 => MemoryMarshal.Read<uint>(valueBytes).ToString(CultureInfo.InvariantCulture),
                0x0a => MemoryMarshal.Read<long>(valueBytes).ToString(CultureInfo.InvariantCulture),
                0x0b => MemoryMarshal.Read<ulong>(valueBytes).ToString(CultureInfo.InvariantCulture),
                0x0c => MemoryMarshal.Read<float>(valueBytes).ToString("F6", CultureInfo.InvariantCulture),
                0x0d => MemoryMarshal.Read<double>(valueBytes).ToString("F6", CultureInfo.InvariantCulture),
                _ => null,
            };
            if (text != null)
            {
                console.Write(!align || (uint)field.Type is 0x0c or 0x0d ? text : text.PadLeft(width));
                return;
            }

            ulong pointer = size == 4 ? MemoryMarshal.Read<uint>(valueBytes) : MemoryMarshal.Read<ulong>(valueBytes);
            if (pointer != 0 && (uint)field.Type == 0x11)
            {
                string value = FormatDacAddress(address, memory.PointerSize);
                string methodTable = FormatDacAddress(field.MTOfType, memory.PointerSize);
                console.WriteDmlExec(value, dml ? $"!DumpVC /d {methodTable} {value}" : null, forceDml: dml);
            }
            else if (pointer != 0 && (uint)field.Type is 0x0e or 0x0f or 0x12 or 0x14 or 0x1c or 0x1d)
            {
                WriteDacAddressLink(console, pointer, memory.PointerSize, "DumpObj", dml, forceDml: dml);
            }
            else
            {
                console.Write(FormatDacAddress(pointer, memory.PointerSize, upperCase: targetOS == OSPlatform.Windows));
            }
        }

        private unsafe void WriteThinLock(ulong address)
        {
            const uint IsHashOrSyncBlockIndex = 0x08000000;
            const uint SpinLock = 0x10000000;
            const uint ThreadIdMask = 0x3ff;
            const uint RecursionMask = 0xfc00;
            if (address < sizeof(uint) || !memory.ReadDword(address - sizeof(uint), out uint header) || (header & (IsHashOrSyncBlockIndex | SpinLock)) != 0)
            {
                return;
            }

            uint threadId = header & ThreadIdMask;
            ClrDataAddress thread = 0;
            if (threadId != 0 && dac.GetThreadFromThinlockID(threadId, &thread) == 0 && thread != 0)
            {
                string pointer = FormatDacAddress(thread, memory.PointerSize, upperCase: targetOS == OSPlatform.Windows);
                console.WriteLine($"ThinLock owner {threadId:x} ({pointer}), Recursive {(header & RecursionMask) >> 10:x}");
            }
        }
        private void WriteSize(ulong size) => console.WriteLine($"Size:        {size}(0x{size:x}) bytes");

        private void WriteAddress(string label, ulong address, string command)
        {
            console.Write(label);
            WriteDacAddressLink(console, address, memory.PointerSize, command, dml, forceDml: dml);
            console.WriteLine();
        }

        private unsafe void WriteString(ulong address, ulong dacAddress, ulong size)
        {
            console.Write("String:      ");
            if (!memory.ReadDword(address + (uint)memory.PointerSize, out uint length))
            {
                console.WriteLine("Error getting string data\n");
                return;
            }
            if (size > MaxStringSize || length > MaxStringSize)
            {
                console.WriteLine("<String is invalid or too large to print>\n");
                return;
            }
            char[] chars = new char[length + 1];
            fixed (char* buffer = chars)
            {
                console.WriteLine(dac.GetObjectStringData(dacAddress, length + 1, buffer, null) == 0 ? ReadNullTerminatedString(chars) : "<Invalid Object>");
            }
        }

        private void WriteArray(ulong address, DacpObjectData data)
        {
            string elementType = data.ElementType < s_elementTypeNames.Length ? s_elementTypeNames[data.ElementType] : "";
            console.Write($"Array:       Rank {data.dwRank}, Number of elements {data.dwNumComponents}, Type {elementType}");
            if (dml)
            {
                console.Write(" (");
                console.WriteDmlExec("Print Array", $"!DumpArray /d {FormatDacAddress(address, memory.PointerSize)}", forceDml: true);
                console.Write(")");
            }
            console.WriteLine();

            const uint CharElement = 0x03;
            const uint SByteElement = 0x04;
            const uint ByteElement = 0x05;
            if (data.ElementType is not (CharElement or SByteElement or ByteElement))
            {
                return;
            }

            bool wide = data.ElementType == CharElement;
            int count = (int)Math.Min(data.dwNumComponents, 128);
            int unitSize = wide ? sizeof(char) : sizeof(byte);
            ulong contentAddress = ToTargetAddress(data.ArrayDataPtr, memory.PointerSize);
            console.WriteDmlExec("Content", dml ? $"{(wide ? "dw" : "db")} {contentAddress:x} L{data.dwNumComponents:x}" : null, forceDml: dml);
            console.Write(":     ");
            Span<byte> bytes = stackalloc byte[count * unitSize];
            if (!bytes.IsEmpty && contentAddress != 0 && memory.ReadMemory(contentAddress, bytes, out int bytesRead))
            {
                Span<char> text = stackalloc char[bytesRead / unitSize];
                for (int i = 0; i < text.Length; i++)
                {
                    char c = wide ? MemoryMarshal.Read<char>(bytes[(i * unitSize)..]) : (char)bytes[i];
                    text[i] = c < 32 || (c > 126 && c <= 255) ? '.' : c;
                }
                console.Write(text.ToString());
            }
            console.WriteLine();
        }
    }
}
