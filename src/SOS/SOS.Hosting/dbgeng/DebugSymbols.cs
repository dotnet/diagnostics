// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Hosting
{
    internal unsafe class DebugSymbols
    {
        internal DebugSymbols(DebugClient client, SOSHost soshost)
        {
            VTableBuilder builder = client.AddInterface(typeof(IDebugSymbols).GUID, validate: true);
            AddDebugSymbols(builder, soshost);
            builder.Complete();

            builder = client.AddInterface(typeof(IDebugSymbols2).GUID, validate: true);
            AddDebugSymbols2(builder, soshost);
            builder.Complete();

            builder = client.AddInterface(typeof(IDebugSymbols3).GUID, validate: true);
            AddDebugSymbols3(builder, soshost);
            builder.Complete();
        }

        private static void AddDebugSymbols(VTableBuilder builder, SOSHost soshost)
        {
            builder.AddMethod(new GetSymbolOptionsDelegate(soshost.GetSymbolOptions));
            builder.AddMethod(new AddSymbolOptionsDelegate((self, options) => HResult.S_OK));
            builder.AddMethod(new RemoveSymbolOptionsDelegate((self, options) => HResult.S_OK));
            builder.AddMethod(new SetSymbolOptionsDelegate((self, options) => HResult.S_OK));
            builder.AddMethod(new GetNameByOffsetDelegate(soshost.GetNameByOffset));
            builder.AddMethod(new GetOffsetByNameDelegate((self, symbol, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNearNameByOffsetDelegate((self, offset, delta, nameBuffer, nameBufferSize, nameSize, displacement) => DebugClient.NotImplemented));
            builder.AddMethod(new GetLineByOffsetDelegate(soshost.GetLineByOffset));
            builder.AddMethod(new GetOffsetByLineDelegate((self, line, file, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNumberModulesDelegate(soshost.GetNumberModules));
            builder.AddMethod(new GetModuleByIndexDelegate(soshost.GetModuleByIndex));
            builder.AddMethod(new GetModuleByModuleNameDelegate(soshost.GetModuleByModuleName));
            builder.AddMethod(new GetModuleByOffsetDelegate(soshost.GetModuleByOffset));
            builder.AddMethod(new GetModuleNamesDelegate(soshost.GetModuleNames));
            builder.AddMethod(new GetModuleParametersDelegate(soshost.GetModuleParameters));
            builder.AddMethod(new GetSymbolModuleDelegate((self, symbol, baseAddress) => DebugClient.NotImplemented));
            builder.AddMethod(new GetTypeNameDelegate((self, module, typeId, nameBuffer, nameBufferSize, nameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetTypeIdDelegate((self, module, name, typeId) => DebugClient.NotImplemented));
            builder.AddMethod(new GetTypeSizeDelegate((self, module, typeId, size) => DebugClient.NotImplemented));
            builder.AddMethod(new GetFieldOffsetDelegate((self, module, typeId, field, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolTypeIdDelegate((self, symbol, typeId, module) => DebugClient.NotImplemented));
            builder.AddMethod(new GetOffsetTypeIdDelegate((self, offset, typeId, module) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadTypedDataVirtualDelegate((self, offset, module, typeid, buffer, buffersize, bytesRead) => DebugClient.NotImplemented));
            builder.AddMethod(new WriteTypedDataVirtualDelegate((self, offset, module, typeid, buffer, buffersize, bytesWritten) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputTypedDataVirtualDelegate((self, outputControl, offset, module, typeId, flags) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadTypedDataPhysicalDelegate((self, offset, module, typeid, buffer, buffersize, bytesRead) => DebugClient.NotImplemented));
            builder.AddMethod(new WriteTypedDataPhysicalDelegate((self, offset, module, typeid, buffer, buffersize, bytesWritten) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputTypedDataPhysicalDelegate((self, outputControl, offset, module, typeId, flags) => DebugClient.NotImplemented));
            builder.AddMethod(new GetScopeDelegate((self, instructionOffset, scopeFrame, scopeContext, scopeContextSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetScopeDelegate((IntPtr self, ulong instructionOffset, ref DEBUG_STACK_FRAME scopeFrame, IntPtr scopeContext, uint scopeContextSize) => DebugClient.NotImplemented));
            builder.AddMethod(new ResetScopeDelegate((self) => DebugClient.NotImplemented));
            builder.AddMethod(new GetScopeSymbolGroupDelegate((self, flags, update, symbols) => DebugClient.NotImplemented));
            builder.AddMethod(new CreateSymbolGroupDelegate((self, group) => DebugClient.NotImplemented));
            builder.AddMethod(new StartSymbolMatchDelegate((self, pattern, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNextSymbolMatchDelegate((self, handle, buffer, bufferSize, matchSize, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new EndSymbolMatchDelegate((self, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new ReloadDelegate((self, module) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolPathDelegate(soshost.GetSymbolPath));
            builder.AddMethod(new SetSymbolPathDelegate((self, path) => DebugClient.NotImplemented));
            builder.AddMethod(new AppendSymbolPathDelegate((self, addition) => DebugClient.NotImplemented));
            builder.AddMethod(new GetImagePathDelegate((self, buffer, bufferSize, pathSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetImagePathDelegate((self, path) => DebugClient.NotImplemented));
            builder.AddMethod(new AppendImagePathDelegate((self, path) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourcePathDelegate((self, buffer, bufferSize, pathSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourcePathElementDelegate((self, index, buffer, bufferSize, elementSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetSourcePathDelegate((self, path) => DebugClient.NotImplemented));
            builder.AddMethod(new AppendSourcePathDelegate((self, addition) => DebugClient.NotImplemented));
            builder.AddMethod(new FindSourceFileDelegate(soshost.FindSourceFile));
            builder.AddMethod(new GetSourceFileLineOffsetsDelegate((self, file, buffer, bufferLines, fileLines) => DebugClient.NotImplemented));
        }

        private static void AddDebugSymbols2(VTableBuilder builder, SOSHost soshost)
        {
            AddDebugSymbols(builder, soshost);
            builder.AddMethod(new GetModuleVersionInformationDelegate(soshost.GetModuleVersionInformation));
            builder.AddMethod(new GetModuleNameStringDelegate((self, which, index, baseAddress, buffer, bufferSize, nameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetConstantNameDelegate((self, module, typeId, value, buffer, bufferSize, nameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetFieldNameDelegate((self, module, typeId, fieldIndex, buffer, bufferSize, nameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetTypeOptionsDelegate((self, options) => DebugClient.NotImplemented));
            builder.AddMethod(new AddTypeOptionsDelegate((self, options) => DebugClient.NotImplemented));
            builder.AddMethod(new RemoveTypeOptionsDelegate((self, options) => DebugClient.NotImplemented));
            builder.AddMethod(new SetTypeOptionsDelegate((self, options) => DebugClient.NotImplemented));
        }

        private static void AddDebugSymbols3(VTableBuilder builder, SOSHost soshost)
        {
            AddDebugSymbols2(builder, soshost);
            builder.AddMethod(new GetNameByOffsetWideDelegate(soshost.GetNameByOffset));
            builder.AddMethod(new GetOffsetByNameWideDelegate((self, symbol, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNearNameByOffsetWideDelegate((self, offset, delta, nameBuffer, nameBufferSize, nameSize, displacement) => DebugClient.NotImplemented));
            builder.AddMethod(new GetLineByOffsetWideDelegate((self, offset, line, fileBuffer, fileBufferSize, fileSize, displacement) => DebugClient.NotImplemented));
            builder.AddMethod(new GetOffsetByLineWideDelegate((self, line, file, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetModuleByModuleNameWideDelegate(soshost.GetModuleByModuleName));
            builder.AddMethod(new GetSymbolModuleWideDelegate((self, symbol, baseAddress) => DebugClient.NotImplemented));
            builder.AddMethod(new GetTypeNameWideDelegate((self, module, typeId, nameBuffer, nameBufferSize, nameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetTypeIdWideDelegate((self, module, name, typeId) => DebugClient.NotImplemented));
            builder.AddMethod(new GetFieldOffsetWideDelegate((self, module, typeId, field, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolTypeIdWideDelegate((self, symbol, typeId, module) => DebugClient.NotImplemented));
            builder.AddMethod(new GetScopeSymbolGroup2Delegate((self, flags, update, symbols) => DebugClient.NotImplemented));
            builder.AddMethod(new CreateSymbolGroup2Delegate((self, group) => DebugClient.NotImplemented));
            builder.AddMethod(new StartSymbolMatchWideDelegate((self, pattern, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNextSymbolMatchWideDelegate((self, handle, buffer, buffesSize, matchSize, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new ReloadWideDelegate((self, module) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolPathWideDelegate(soshost.GetSymbolPath));
            builder.AddMethod(new SetSymbolPathWideDelegate((self, path) => DebugClient.NotImplemented));
            builder.AddMethod(new AppendSymbolPathWideDelegate((self, addition) => DebugClient.NotImplemented));
            builder.AddMethod(new GetImagePathWideDelegate((self, buffer, bufferSize, pathSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetImagePathWideDelegate((self, path) => DebugClient.NotImplemented));
            builder.AddMethod(new AppendImagePathWideDelegate((self, path) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourcePathWideDelegate((self, buffer, bufferSize, pathSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourcePathElementWideDelegate((self, index, buffer, bufferSize, elementSize) => DebugClient.NotImplemented));
            builder.AddMethod(new SetSourcePathWideDelegate((self, path) => DebugClient.NotImplemented));
            builder.AddMethod(new AppendSourcePathWideDelegate((self, addition) => DebugClient.NotImplemented));
            builder.AddMethod(new FindSourceFileWideDelegate(soshost.FindSourceFile));
            builder.AddMethod(new GetSourceFileLineOffsetsWideDelegate((self, file, buffer, bufferLines, fileLines) => DebugClient.NotImplemented));
            builder.AddMethod(new GetModuleVersionInformationWideDelegate((self, index, baseAddress, item, buffer, bufferSize, verInfoSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetModuleNameStringWideDelegate((self, which, index, baseAddress, buffer, bufferSize, nameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetConstantNameWideDelegate((self, module, typeId, value, buffer, bufferSize, nameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetFieldNameWideDelegate((self, module, typeId, fieldIndex, buffer, bufferSize, nameSize) => DebugClient.NotImplemented));
            builder.AddMethod(new IsManagedModuleDelegate((self, index, baseAddress) => DebugClient.NotImplemented));
            builder.AddMethod(new GetModuleByModuleName2Delegate((self, name, startIndex, flags, index, baseAddress) => DebugClient.NotImplemented));
            builder.AddMethod(new GetModuleByModuleName2WideDelegate((self, name, startIndex, flags, index, baseAddress) => DebugClient.NotImplemented));
            builder.AddMethod(new GetModuleByOffset2Delegate((self, offset, startIndex, flags, index, baseAddress) => DebugClient.NotImplemented));
            builder.AddMethod(new AddSyntheticModuleDelegate((self, baseAddress, size, imagePath, moduleName, flags) => DebugClient.NotImplemented));
            builder.AddMethod(new AddSyntheticModuleWideDelegate((self, baseAddress, size, imagePath, moduleName, flags) => DebugClient.NotImplemented));
            builder.AddMethod(new RemoveSyntheticModuleDelegate((self, baseAddress) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentScopeFrameIndexDelegate((self, index) => DebugClient.NotImplemented));
            builder.AddMethod(new SetScopeFrameByIndexDelegate((self, index) => DebugClient.NotImplemented));
            builder.AddMethod(new SetScopeFromJitDebugInfoDelegate((self, outputControl, infoOffset) => DebugClient.NotImplemented));
            builder.AddMethod(new SetScopeFromStoredEventDelegate((self) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputSymbolByOffsetDelegate((self, outputControl, flags, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetFunctionEntryByOffsetDelegate((self, offset, flags, buffer, buffersize, bufferNeeded) => DebugClient.NotImplemented));
            builder.AddMethod(new GetFieldTypeAndOffsetDelegate((self, module, containerTypeId, field, fieldTypeId, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetFieldTypeAndOffsetWideDelegate((self, module, containerTypeId, field, fieldTypeId, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new AddSyntheticSymbolDelegate((self, offset, size, name, flags, id) => DebugClient.NotImplemented));
            builder.AddMethod(new AddSyntheticSymbolWideDelegate((self, offset, size, name, flags, id) => DebugClient.NotImplemented));
            builder.AddMethod(new RemoveSyntheticSymbolDelegate((self, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntriesByOffsetDelegate((self, offset, flags, ids, displacement, idsCount, entries) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntriesByNameDelegate((self, symbol, flags, ids, idsCount, entries) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntriesByNameWideDelegate((self, symbol, flags, ids, idsCount, entries) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntryByTokenDelegate((self, moduleBase, token, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntryInformationDelegate((self, id, info) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntryStringDelegate((self, id, which, buffer, bufferSize, stringSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntryStringWideDelegate((self, id, which, buffer, bufferSize, stringSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntryOffsetRegionsDelegate((self, id, flags, regions, regionsCount, regionsAvail) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSymbolEntryBySymbolEntryDelegate((self, fromId, flags, toId) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourceEntriesByOffsetDelegate((self, offset, flags, entries, entriesCount, entriesAvail) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourceEntriesByLineDelegate((self, line, file, flags, entries, entriesCount, entriesAvail) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourceEntriesByLineWideDelegate((self, line, file, flags, entries, entriesCount, entriesAvail) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourceEntryStringDelegate((self, entry, which, buffer, bufferSize, stringSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourceEntryStringWideDelegate((self, entry, which, buffer, bufferSize, stringSize) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourceEntryOffsetRegionsDelegate((self, entry, flags, regions, regionsCount, regionsAvail) => DebugClient.NotImplemented));
            builder.AddMethod(new GetSourceEntryBySourceEntryDelegate((self, fromEntry, flags, toEntry) => DebugClient.NotImplemented));
        }

        #region IDebugSymbols Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolOptionsDelegate(
            IntPtr self,
            [Out] out SYMOPT Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddSymbolOptionsDelegate(
            IntPtr self,
            [In] SYMOPT Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveSymbolOptionsDelegate(
            IntPtr self,
            [In] SYMOPT Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetSymbolOptionsDelegate(
            IntPtr self,
            [In] SYMOPT Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate int GetNameByOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            [In] uint NameBufferSize,
            [Out] uint* NameSize,
            [Out] ulong* Displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOffsetByNameDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNearNameByOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] int Delta,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            [In] int NameBufferSize,
            [Out] uint* NameSize,
            [Out] ulong* Displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetLineByOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] uint* Line,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FileBuffer,
            [In] uint FileBufferSize,
            [Out] uint* FileSize,
            [Out] ulong* Displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOffsetByLineDelegate(
            IntPtr self,
            [In] uint Line,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberModulesDelegate(
            IntPtr self,
            [Out] out uint Loaded,
            [Out] out uint Unloaded);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByIndexDelegate(
            IntPtr self,
            [In] uint Index,
            [Out] out ulong Base);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByModuleNameDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            [In] uint StartIndex,
            [Out] uint* Index,
            [Out] ulong* Base);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] uint StartIndex,
            [Out] uint* Index,
            [Out] ulong* Base);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleNamesDelegate(
            IntPtr self,
            [In] uint Index,
            [In] ulong Base,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder ImageNameBuffer,
            [In] uint ImageNameBufferSize,
            [Out] uint* ImageNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder ModuleNameBuffer,
            [In] uint ModuleNameBufferSize,
            [Out] uint* ModuleNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder LoadedImageNameBuffer,
            [In] uint LoadedImageNameBufferSize,
            [Out] uint* LoadedImageNameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleParametersDelegate(
            IntPtr self,
            [In] uint Count,
            [In] ulong* Bases,
            [In] uint Start,
            [Out] DEBUG_MODULE_PARAMETERS* Params);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolModuleDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            [Out] ulong* Base);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTypeNameDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            [In] int NameBufferSize,
            [Out] uint* NameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTypeIdDelegate(
            IntPtr self,
            [In] ulong Module,
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            [Out] uint* TypeId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTypeSizeDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [Out] uint* Size);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFieldOffsetDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [In][MarshalAs(UnmanagedType.LPStr)] string Field,
            [Out] uint* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolTypeIdDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            [Out] uint* TypeId,
            [Out] ulong* Module);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOffsetTypeIdDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] uint* TypeId,
            [Out] ulong* Module);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadTypedDataVirtualDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] ulong Module,
            [In] uint TypeId,
            [Out] byte* Buffer,
            [In] uint BufferSize,
            [Out] uint* BytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteTypedDataVirtualDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] ulong Module,
            [In] uint TypeId,
            [In] IntPtr Buffer,
            [In] uint BufferSize,
            [Out] uint* BytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputTypedDataVirtualDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] ulong Offset,
            [In] ulong Module,
            [In] uint TypeId,
            [In] DEBUG_TYPEOPTS Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadTypedDataPhysicalDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] ulong Module,
            [In] uint TypeId,
            [In] IntPtr Buffer,
            [In] uint BufferSize,
            [Out] uint* BytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteTypedDataPhysicalDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] ulong Module,
            [In] uint TypeId,
            [In] IntPtr Buffer,
            [In] uint BufferSize,
            [Out] uint* BytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputTypedDataPhysicalDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] ulong Offset,
            [In] ulong Module,
            [In] uint TypeId,
            [In] DEBUG_TYPEOPTS Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetScopeDelegate(
            IntPtr self,
            [Out] ulong* InstructionOffset,
            [Out] DEBUG_STACK_FRAME* ScopeFrame,
            [In] IntPtr ScopeContext,
            [In] uint ScopeContextSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetScopeDelegate(
            IntPtr self,
            [In] ulong InstructionOffset,
            [In] ref DEBUG_STACK_FRAME ScopeFrame,
            [In] IntPtr ScopeContext,
            [In] uint ScopeContextSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ResetScopeDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetScopeSymbolGroupDelegate(
            IntPtr self,
            [In] DEBUG_SCOPE_GROUP Flags,
            [In][MarshalAs(UnmanagedType.Interface)] IDebugSymbolGroup Update,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr Symbols);            // out IDebugSymbolGroup

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CreateSymbolGroupDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr Group);              // out IDebugSymbolGroup

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int StartSymbolMatchDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Pattern,
            [Out] ulong* Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNextSymbolMatchDelegate(
            IntPtr self,
            [In] ulong Handle,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* MatchSize,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int EndSymbolMatchDelegate(
            IntPtr self,
            [In] ulong Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReloadDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Module);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolPathDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* PathSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetSymbolPathDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AppendSymbolPathDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetImagePathDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* PathSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetImagePathDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AppendImagePathDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourcePathDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* PathSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourcePathElementDelegate(
            IntPtr self,
            [In] uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* ElementSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetSourcePathDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AppendSourcePathDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FindSourceFileDelegate(
            IntPtr self,
            [In] uint StartElement,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [In] DEBUG_FIND_SOURCE Flags,
            [Out] uint* FoundElement,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] uint BufferSize,
            [Out] uint* FoundSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceFileLineOffsetsDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [Out] ulong* Buffer,
            [In] int BufferLines,
            [Out] uint* FileLines);

        #endregion

        #region IDebugSymbols2 Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleVersionInformationDelegate(
            IntPtr self,
            [In] uint Index,
            [In] ulong Base,
            [In][MarshalAs(UnmanagedType.LPStr)] string Item,
            [Out] byte* Buffer,
            [In] uint BufferSize,
            [Out] uint* VerInfoSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleNameStringDelegate(
            IntPtr self,
            [In] DEBUG_MODNAME Which,
            [In] uint Index,
            [In] ulong Base,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] uint BufferSize,
            [Out] uint* NameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetConstantNameDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [In] ulong Value,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* NameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFieldNameDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [In] uint FieldIndex,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* NameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTypeOptionsDelegate(
            IntPtr self,
            [Out] DEBUG_TYPEOPTS* Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddTypeOptionsDelegate(
            IntPtr self,
            [In] DEBUG_TYPEOPTS Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveTypeOptionsDelegate(
            IntPtr self,
            [In] DEBUG_TYPEOPTS Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetTypeOptionsDelegate(
            IntPtr self,
            [In] DEBUG_TYPEOPTS Options);

        #endregion

        #region IDebugSymbols3 Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNameByOffsetWideDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            [In] uint NameBufferSize,
            [Out] uint* NameSize,
            [Out] ulong* Displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOffsetByNameWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Symbol,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNearNameByOffsetWideDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] int Delta,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            [In] int NameBufferSize,
            [Out] uint* NameSize,
            [Out] ulong* Displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetLineByOffsetWideDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] uint* Line,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder FileBuffer,
            [In] int FileBufferSize,
            [Out] uint* FileSize,
            [Out] ulong* Displacement);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOffsetByLineWideDelegate(
            IntPtr self,
            [In] uint Line,
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByModuleNameWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            [In] uint StartIndex,
            [Out] uint* Index,
            [Out] ulong* Base);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolModuleWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Symbol,
            [Out] ulong* Base);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTypeNameWideDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            [In] int NameBufferSize,
            [Out] uint* NameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTypeIdWideDelegate(
            IntPtr self,
            [In] ulong Module,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            [Out] uint* TypeId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFieldOffsetWideDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Field,
            [Out] uint* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolTypeIdWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Symbol,
            [Out] uint* TypeId,
            [Out] ulong* Module);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetScopeSymbolGroup2Delegate(
            IntPtr self,
            [In] DEBUG_SCOPE_GROUP Flags,
            [In][MarshalAs(UnmanagedType.Interface)] IDebugSymbolGroup2 Update,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr Symbols);            // out IDebugSymbolGroup2

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CreateSymbolGroup2Delegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr Group);              // out IDebugSymbolGroup2

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int StartSymbolMatchWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Pattern,
            [Out] ulong* Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNextSymbolMatchWideDelegate(
            IntPtr self,
            [In] ulong Handle,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* MatchSize,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReloadWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Module);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolPathWideDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* PathSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetSymbolPathWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Path);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AppendSymbolPathWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Addition);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetImagePathWideDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* PathSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetImagePathWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Path);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AppendImagePathWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Addition);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourcePathWideDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* PathSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourcePathElementWideDelegate(
            IntPtr self,
            [In] uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* ElementSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetSourcePathWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Path);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AppendSourcePathWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Addition);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FindSourceFileWideDelegate(
            IntPtr self,
            [In] uint StartElement,
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            [In] DEBUG_FIND_SOURCE Flags,
            [Out] uint* FoundElement,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] uint BufferSize,
            [Out] uint* FoundSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceFileLineOffsetsWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            [Out] ulong* Buffer,
            [In] int BufferLines,
            [Out] uint* FileLines);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleVersionInformationWideDelegate(
            IntPtr self,
            [In] uint Index,
            [In] ulong Base,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Item,
            [In] IntPtr Buffer,
            [In] int BufferSize,
            [Out] uint* VerInfoSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleNameStringWideDelegate(
            IntPtr self,
            [In] DEBUG_MODNAME Which,
            [In] uint Index,
            [In] ulong Base,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* NameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetConstantNameWideDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [In] ulong Value,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* NameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFieldNameWideDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint TypeId,
            [In] uint FieldIndex,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* NameSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int IsManagedModuleDelegate(
            IntPtr self,
            [In] uint Index,
            [In] ulong Base
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByModuleName2Delegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            [In] uint StartIndex,
            [In] DEBUG_GETMOD Flags,
            [Out] uint* Index,
            [Out] ulong* Base
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByModuleName2WideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            [In] uint StartIndex,
            [In] DEBUG_GETMOD Flags,
            [Out] uint* Index,
            [Out] ulong* Base
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetModuleByOffset2Delegate(
            IntPtr self,
            [In] ulong Offset,
            [In] uint StartIndex,
            [In] DEBUG_GETMOD Flags,
            [Out] uint* Index,
            [Out] ulong* Base
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddSyntheticModuleDelegate(
            IntPtr self,
            [In] ulong Base,
            [In] uint Size,
            [In][MarshalAs(UnmanagedType.LPStr)] string ImagePath,
            [In][MarshalAs(UnmanagedType.LPStr)] string ModuleName,
            [In] DEBUG_ADDSYNTHMOD Flags
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddSyntheticModuleWideDelegate(
            IntPtr self,
            [In] ulong Base,
            [In] uint Size,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ImagePath,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ModuleName,
            [In] DEBUG_ADDSYNTHMOD Flags
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveSyntheticModuleDelegate(
            IntPtr self,
            [In] ulong Base
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentScopeFrameIndexDelegate(
            IntPtr self,
            [Out] uint* Index
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetScopeFrameByIndexDelegate(
            IntPtr self,
            [In] uint Index
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetScopeFromJitDebugInfoDelegate(
            IntPtr self,
            [In] uint OutputControl,
            [In] ulong InfoOffset
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetScopeFromStoredEventDelegate(
            IntPtr self
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputSymbolByOffsetDelegate(
            IntPtr self,
            [In] uint OutputControl,
            [In] DEBUG_OUTSYM Flags,
            [In] ulong Offset
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFunctionEntryByOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] DEBUG_GETFNENT Flags,
            [In] IntPtr Buffer,
            [In] uint BufferSize,
            [Out] uint* BufferNeeded
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFieldTypeAndOffsetDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint ContainerTypeId,
            [In][MarshalAs(UnmanagedType.LPStr)] string Field,
            [Out] uint* FieldTypeId,
            [Out] uint* Offset
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFieldTypeAndOffsetWideDelegate(
            IntPtr self,
            [In] ulong Module,
            [In] uint ContainerTypeId,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Field,
            [Out] uint* FieldTypeId,
            [Out] uint* Offset
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddSyntheticSymbolDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] uint Size,
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            [In] DEBUG_ADDSYNTHSYM Flags,
            [Out] DEBUG_MODULE_AND_ID* Id
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddSyntheticSymbolWideDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] uint Size,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            [In] DEBUG_ADDSYNTHSYM Flags,
            [Out] DEBUG_MODULE_AND_ID* Id
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveSyntheticSymbolDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_MODULE_AND_ID Id
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntriesByOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] uint Flags,
            [Out] DEBUG_MODULE_AND_ID* Ids,
            [Out] ulong* Displacements,
            [In] uint IdsCount,
            [Out] uint* Entries
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntriesByNameDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            [In] uint Flags,
            [Out] DEBUG_MODULE_AND_ID* Ids,
            [In] uint IdsCount,
            [Out] uint* Entries
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntriesByNameWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Symbol,
            [In] uint Flags,
            [Out] DEBUG_MODULE_AND_ID* Ids,
            [In] uint IdsCount,
            [Out] uint* Entries
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntryByTokenDelegate(
            IntPtr self,
            [In] ulong ModuleBase,
            [In] uint Token,
            [Out] DEBUG_MODULE_AND_ID* Id
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntryInformationDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_MODULE_AND_ID Id,
            [Out] DEBUG_SYMBOL_ENTRY* Info
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntryStringDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_MODULE_AND_ID Id,
            [In] uint Which,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* StringSize
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntryStringWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_MODULE_AND_ID Id,
            [In] uint Which,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* StringSize
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntryOffsetRegionsDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_MODULE_AND_ID Id,
            [In] uint Flags,
            [Out] DEBUG_OFFSET_REGION* Regions,
            [In] uint RegionsCount,
            [Out] uint* RegionsAvail
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSymbolEntryBySymbolEntryDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_MODULE_AND_ID FromId,
            [In] uint Flags,
            [Out] DEBUG_MODULE_AND_ID* ToId
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceEntriesByOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] uint Flags,
            [Out] DEBUG_SYMBOL_SOURCE_ENTRY* Entries,
            [In] uint EntriesCount,
            [Out] uint* EntriesAvail
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceEntriesByLineDelegate(
            IntPtr self,
            [In] uint Line,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [In] uint Flags,
            [Out] DEBUG_SYMBOL_SOURCE_ENTRY* Entries,
            [In] uint EntriesCount,
            [Out] uint* EntriesAvail
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceEntriesByLineWideDelegate(
            IntPtr self,
            [In] uint Line,
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            [In] uint Flags,
            [Out] DEBUG_SYMBOL_SOURCE_ENTRY* Entries,
            [In] uint EntriesCount,
            [Out] uint* EntriesAvail
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceEntryStringDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_SYMBOL_SOURCE_ENTRY Entry,
            [In] uint Which,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* StringSize
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceEntryStringWideDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_SYMBOL_SOURCE_ENTRY Entry,
            [In] uint Which,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* StringSize
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceEntryOffsetRegionsDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_SYMBOL_SOURCE_ENTRY Entry,
            [In] uint Flags,
            [Out] DEBUG_OFFSET_REGION* Regions,
            [In] uint RegionsCount,
            [Out] uint* RegionsAvail
        );

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSourceEntryBySourceEntryDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStruct)] DEBUG_SYMBOL_SOURCE_ENTRY FromEntry,
            [In] uint Flags,
            [Out] DEBUG_SYMBOL_SOURCE_ENTRY* ToEntry
        );

        #endregion
    }
}