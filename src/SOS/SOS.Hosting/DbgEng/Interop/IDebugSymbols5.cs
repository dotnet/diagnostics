// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("c65fa83e-1e69-475e-8e0e-b5d79e9cc17e")]
    public interface IDebugSymbols5 : IDebugSymbols4
    {
        /* IDebugSymbols */

        [PreserveSig]
        new int GetSymbolOptions(
            out SYMOPT Options);

        [PreserveSig]
        new int AddSymbolOptions(
            SYMOPT Options);

        [PreserveSig]
        new int RemoveSymbolOptions(
            SYMOPT Options);

        [PreserveSig]
        new int SetSymbolOptions(
            SYMOPT Options);

        [PreserveSig]
        new int GetNameByOffset(
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong Displacement);

        [PreserveSig]
        new int GetOffsetByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            out ulong Offset);

        [PreserveSig]
        new int GetNearNameByOffset(
            ulong Offset,
            int Delta,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong Displacement);

        [PreserveSig]
        new int GetLineByOffset(
            ulong Offset,
            out uint Line,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FileBuffer,
            int FileBufferSize,
            out uint FileSize,
            out ulong Displacement);

        [PreserveSig]
        new int GetOffsetByLine(
            uint Line,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            out ulong Offset);

        [PreserveSig]
        new int GetNumberModules(
            out uint Loaded,
            out uint Unloaded);

        [PreserveSig]
        new int GetModuleByIndex(
            uint Index,
            out ulong Base);

        [PreserveSig]
        new int GetModuleByModuleName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            uint StartIndex,
            out uint Index,
            out ulong Base);

        [PreserveSig]
        new int GetModuleByOffset(
            ulong Offset,
            uint StartIndex,
            out uint Index,
            out ulong Base);

        [PreserveSig]
        new int GetModuleNames(
            uint Index,
            ulong Base,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder ImageNameBuffer,
            int ImageNameBufferSize,
            out uint ImageNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder ModuleNameBuffer,
            int ModuleNameBufferSize,
            out uint ModuleNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder LoadedImageNameBuffer,
            int LoadedImageNameBufferSize,
            out uint LoadedImageNameSize);

        [PreserveSig]
        new int GetModuleParameters(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] ulong[] Bases,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_MODULE_PARAMETERS[] Params);

        [PreserveSig]
        new int GetSymbolModule(
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            out ulong Base);

        [PreserveSig]
        new int GetTypeName(
            ulong Module,
            uint TypeId,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize);

        [PreserveSig]
        new int GetTypeId(
            ulong Module,
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            out uint TypeId);

        [PreserveSig]
        new int GetTypeSize(
            ulong Module,
            uint TypeId,
            out uint Size);

        [PreserveSig]
        new int GetFieldOffset(
            ulong Module,
            uint TypeId,
            [In][MarshalAs(UnmanagedType.LPStr)] string Field,
            out uint Offset);

        [PreserveSig]
        new int GetSymbolTypeId(
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            out uint TypeId,
            out ulong Module);

        [PreserveSig]
        new int GetOffsetTypeId(
            ulong Offset,
            out uint TypeId,
            out ulong Module);

        [PreserveSig]
        new int ReadTypedDataVirtual(
            ulong Offset,
            ulong Module,
            uint TypeId,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
            byte[] Buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        new int WriteTypedDataVirtual(
            ulong Offset,
            ulong Module,
            uint TypeId,
            IntPtr Buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        new int OutputTypedDataVirtual(
            DEBUG_OUTCTL OutputControl,
            ulong Offset,
            ulong Module,
            uint TypeId,
            DEBUG_TYPEOPTS Flags);

        [PreserveSig]
        new int ReadTypedDataPhysical(
            ulong Offset,
            ulong Module,
            uint TypeId,
            IntPtr Buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        new int WriteTypedDataPhysical(
            ulong Offset,
            ulong Module,
            uint TypeId,
            IntPtr Buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        new int OutputTypedDataPhysical(
            DEBUG_OUTCTL OutputControl,
            ulong Offset,
            ulong Module,
            uint TypeId,
            DEBUG_TYPEOPTS Flags);

        [PreserveSig]
        new int GetScope(
            out ulong InstructionOffset,
            out DEBUG_STACK_FRAME ScopeFrame,
            IntPtr ScopeContext,
            uint ScopeContextSize);

        [PreserveSig]
        new int SetScope(
            ulong InstructionOffset,
            in DEBUG_STACK_FRAME ScopeFrame,
            IntPtr ScopeContext,
            uint ScopeContextSize);

        [PreserveSig]
        new int ResetScope();

        [PreserveSig]
        new int GetScopeSymbolGroup(
            DEBUG_SCOPE_GROUP Flags,
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugSymbolGroup Update,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugSymbolGroup Symbols);

        [PreserveSig]
        new int CreateSymbolGroup(
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugSymbolGroup Group);

        [PreserveSig]
        new int StartSymbolMatch(
            [In][MarshalAs(UnmanagedType.LPStr)] string Pattern,
            out ulong Handle);

        [PreserveSig]
        new int GetNextSymbolMatch(
            ulong Handle,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint MatchSize,
            out ulong Offset);

        [PreserveSig]
        new int EndSymbolMatch(
            ulong Handle);

        [PreserveSig]
        new int Reload(
            [In][MarshalAs(UnmanagedType.LPStr)] string Module);

        [PreserveSig]
        new int GetSymbolPath(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        new int SetSymbolPath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [PreserveSig]
        new int AppendSymbolPath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [PreserveSig]
        new int GetImagePath(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        new int SetImagePath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [PreserveSig]
        new int AppendImagePath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [PreserveSig]
        new int GetSourcePath(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        new int GetSourcePathElement(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint ElementSize);

        [PreserveSig]
        new int SetSourcePath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [PreserveSig]
        new int AppendSourcePath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [PreserveSig]
        new int FindSourceFile(
            uint StartElement,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            DEBUG_FIND_SOURCE Flags,
            out uint FoundElement,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint FoundSize);

        [PreserveSig]
        new int GetSourceFileLineOffsets(
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            ulong[] Buffer,
            int BufferLines,
            out uint FileLines);

        /* IDebugSymbols2 */

        [PreserveSig]
        new int GetModuleVersionInformation(
            uint Index,
            ulong Base,
            [In][MarshalAs(UnmanagedType.LPStr)] string Item,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
            byte[] buffer,
            uint BufferSize,
            out uint VerInfoSize);

        [PreserveSig]
        new int GetModuleNameString(
            DEBUG_MODNAME Which,
            uint Index,
            ulong Base,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            uint BufferSize,
            out uint NameSize);

        [PreserveSig]
        new int GetConstantName(
            ulong Module,
            uint TypeId,
            ulong Value,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        new int GetFieldName(
            ulong Module,
            uint TypeId,
            uint FieldIndex,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        new int GetTypeOptions(
            out DEBUG_TYPEOPTS Options);

        [PreserveSig]
        new int AddTypeOptions(
            DEBUG_TYPEOPTS Options);

        [PreserveSig]
        new int RemoveTypeOptions(
            DEBUG_TYPEOPTS Options);

        [PreserveSig]
        new int SetTypeOptions(
            DEBUG_TYPEOPTS Options);

        /* IDebugSymbols3 */

        [PreserveSig]
        new int GetNameByOffsetWide(
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong Displacement);

        [PreserveSig]
        new int GetOffsetByNameWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Symbol,
            out ulong Offset);

        [PreserveSig]
        new int GetNearNameByOffsetWide(
            ulong Offset,
            int Delta,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong Displacement);

        [PreserveSig]
        new int GetLineByOffsetWide(
            ulong Offset,
            out uint Line,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder FileBuffer,
            int FileBufferSize,
            out uint FileSize,
            out ulong Displacement);

        [PreserveSig]
        new int GetOffsetByLineWide(
            uint Line,
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            out ulong Offset);

        [PreserveSig]
        new int GetModuleByModuleNameWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            uint StartIndex,
            out uint Index,
            out ulong Base);

        [PreserveSig]
        new int GetSymbolModuleWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Symbol,
            out ulong Base);

        [PreserveSig]
        new int GetTypeNameWide(
            ulong Module,
            uint TypeId,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize);

        [PreserveSig]
        new int GetTypeIdWide(
            ulong Module,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            out uint TypeId);

        [PreserveSig]
        new int GetFieldOffsetWide(
            ulong Module,
            uint TypeId,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Field,
            out uint Offset);

        [PreserveSig]
        new int GetSymbolTypeIdWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Symbol,
            out uint TypeId,
            out ulong Module);

        [PreserveSig]
        new int GetScopeSymbolGroup2(
            DEBUG_SCOPE_GROUP Flags,
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugSymbolGroup2 Update,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugSymbolGroup2 Symbols);

        [PreserveSig]
        new int CreateSymbolGroup2(
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugSymbolGroup2 Group);

        [PreserveSig]
        new int StartSymbolMatchWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Pattern,
            out ulong Handle);

        [PreserveSig]
        new int GetNextSymbolMatchWide(
            ulong Handle,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint MatchSize,
            out ulong Offset);

        [PreserveSig]
        new int ReloadWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Module);

        [PreserveSig]
        new int GetSymbolPathWide(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        new int SetSymbolPathWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Path);

        [PreserveSig]
        new int AppendSymbolPathWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Addition);

        [PreserveSig]
        new int GetImagePathWide(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        new int SetImagePathWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Path);

        [PreserveSig]
        new int AppendImagePathWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Addition);

        [PreserveSig]
        new int GetSourcePathWide(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        new int GetSourcePathElementWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint ElementSize);

        [PreserveSig]
        new int SetSourcePathWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Path);

        [PreserveSig]
        new int AppendSourcePathWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Addition);

        [PreserveSig]
        new int FindSourceFileWide(
            uint StartElement,
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            DEBUG_FIND_SOURCE Flags,
            out uint FoundElement,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint FoundSize);

        [PreserveSig]
        new int GetSourceFileLineOffsetsWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            ulong[] Buffer,
            int BufferLines,
            out uint FileLines);

        [PreserveSig]
        new int GetModuleVersionInformationWide(
            uint Index,
            ulong Base,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Item,
            IntPtr Buffer,
            int BufferSize,
            out uint VerInfoSize);

        [PreserveSig]
        new int GetModuleNameStringWide(
            DEBUG_MODNAME Which,
            uint Index,
            ulong Base,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        new int GetConstantNameWide(
            ulong Module,
            uint TypeId,
            ulong Value,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        new int GetFieldNameWide(
            ulong Module,
            uint TypeId,
            uint FieldIndex,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        new int IsManagedModule(
            uint Index,
            ulong Base
        );

        [PreserveSig]
        new int GetModuleByModuleName2(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            uint StartIndex,
            DEBUG_GETMOD Flags,
            out uint Index,
            out ulong Base
        );

        [PreserveSig]
        new int GetModuleByModuleName2Wide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            uint StartIndex,
            DEBUG_GETMOD Flags,
            out uint Index,
            out ulong Base
        );

        [PreserveSig]
        new int GetModuleByOffset2(
            ulong Offset,
            uint StartIndex,
            DEBUG_GETMOD Flags,
            out uint Index,
            out ulong Base
        );

        [PreserveSig]
        new int AddSyntheticModule(
            ulong Base,
            uint Size,
            [In][MarshalAs(UnmanagedType.LPStr)] string ImagePath,
            [In][MarshalAs(UnmanagedType.LPStr)] string ModuleName,
            DEBUG_ADDSYNTHMOD Flags
        );

        [PreserveSig]
        new int AddSyntheticModuleWide(
            ulong Base,
            uint Size,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ImagePath,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ModuleName,
            DEBUG_ADDSYNTHMOD Flags
        );

        [PreserveSig]
        new int RemoveSyntheticModule(
            ulong Base
        );

        [PreserveSig]
        new int GetCurrentScopeFrameIndex(
            out uint Index
        );

        [PreserveSig]
        new int SetScopeFrameByIndex(
            uint Index
        );

        [PreserveSig]
        new int SetScopeFromJitDebugInfo(
            uint OutputControl,
            ulong InfoOffset
        );

        [PreserveSig]
        new int SetScopeFromStoredEvent(
        );

        [PreserveSig]
        new int OutputSymbolByOffset(
            uint OutputControl,
            DEBUG_OUTSYM Flags,
            ulong Offset
        );

        [PreserveSig]
        new int GetFunctionEntryByOffset(
            ulong Offset,
            DEBUG_GETFNENT Flags,
            IntPtr Buffer,
            uint BufferSize,
            out uint BufferNeeded
        );

        [PreserveSig]
        new int GetFieldTypeAndOffset(
            ulong Module,
            uint ContainerTypeId,
            [In][MarshalAs(UnmanagedType.LPStr)] string Field,
            out uint FieldTypeId,
            out uint Offset
        );

        [PreserveSig]
        new int GetFieldTypeAndOffsetWide(
            ulong Module,
            uint ContainerTypeId,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Field,
            out uint FieldTypeId,
            out uint Offset
        );

        [PreserveSig]
        new int AddSyntheticSymbol(
            ulong Offset,
            uint Size,
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            DEBUG_ADDSYNTHSYM Flags,
            out DEBUG_MODULE_AND_ID Id
        );

        [PreserveSig]
        new int AddSyntheticSymbolWide(
            ulong Offset,
            uint Size,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            DEBUG_ADDSYNTHSYM Flags,
            out DEBUG_MODULE_AND_ID Id
        );

        [PreserveSig]
        new int RemoveSyntheticSymbol(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_MODULE_AND_ID Id
        );

        [PreserveSig]
        new int GetSymbolEntriesByOffset(
            ulong Offset,
            uint Flags,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_MODULE_AND_ID[] Ids,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            ulong[] Displacements,
            uint IdsCount,
            out uint Entries
        );

        [PreserveSig]
        new int GetSymbolEntriesByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            uint Flags,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_MODULE_AND_ID[] Ids,
            uint IdsCount,
            out uint Entries
        );

        [PreserveSig]
        new int GetSymbolEntriesByNameWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Symbol,
            uint Flags,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_MODULE_AND_ID[] Ids,
            uint IdsCount,
            out uint Entries
        );

        [PreserveSig]
        new int GetSymbolEntryByToken(
            ulong ModuleBase,
            uint Token,
            out DEBUG_MODULE_AND_ID Id
        );

        [PreserveSig]
        new int GetSymbolEntryInformation(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_MODULE_AND_ID Id,
            out DEBUG_SYMBOL_ENTRY Info
        );

        [PreserveSig]
        new int GetSymbolEntryString(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_MODULE_AND_ID Id,
            uint Which,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint StringSize
        );

        [PreserveSig]
        new int GetSymbolEntryStringWide(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_MODULE_AND_ID Id,
            uint Which,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint StringSize
        );

        [PreserveSig]
        new int GetSymbolEntryOffsetRegions(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_MODULE_AND_ID Id,
            uint Flags,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_OFFSET_REGION[] Regions,
            uint RegionsCount,
            out uint RegionsAvail
        );

        [Obsolete("Do not use: no longer implemented.", true)]
        [PreserveSig]
        new int GetSymbolEntryBySymbolEntry(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_MODULE_AND_ID FromId,
            uint Flags,
            out DEBUG_MODULE_AND_ID ToId
        );

        [PreserveSig]
        new int GetSourceEntriesByOffset(
            ulong Offset,
            uint Flags,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_SYMBOL_SOURCE_ENTRY[] Entries,
            uint EntriesCount,
            out uint EntriesAvail
        );

        [PreserveSig]
        new int GetSourceEntriesByLine(
            uint Line,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            uint Flags,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_SYMBOL_SOURCE_ENTRY[] Entries,
            uint EntriesCount,
            out uint EntriesAvail
        );

        [PreserveSig]
        new int GetSourceEntriesByLineWide(
            uint Line,
            [In][MarshalAs(UnmanagedType.LPWStr)] string File,
            uint Flags,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_SYMBOL_SOURCE_ENTRY[] Entries,
            uint EntriesCount,
            out uint EntriesAvail
        );

        [PreserveSig]
        new int GetSourceEntryString(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_SYMBOL_SOURCE_ENTRY Entry,
            uint Which,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint StringSize
        );

        [PreserveSig]
        new int GetSourceEntryStringWide(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_SYMBOL_SOURCE_ENTRY Entry,
            uint Which,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint StringSize
        );

        [PreserveSig]
        new int GetSourceEntryOffsetRegions(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_SYMBOL_SOURCE_ENTRY Entry,
            uint Flags,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_OFFSET_REGION[] Regions,
            uint RegionsCount,
            out uint RegionsAvail
        );

        [PreserveSig]
        new int GetSourceEntryBySourceEntry(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_SYMBOL_SOURCE_ENTRY FromEntry,
            uint Flags,
            out DEBUG_SYMBOL_SOURCE_ENTRY ToEntry
        );

        /* IDebugSymbols4 */

        [PreserveSig]
        new int GetScopeEx(
            out ulong InstructionOffset,
            out DEBUG_STACK_FRAME_EX ScopeFrame,
            IntPtr ScopeContext,
            uint ScopeContextSize
        );

        [PreserveSig]
        new int SetScopeEx(
            ulong InstructionOffset,
            [In][MarshalAs(UnmanagedType.LPStruct)]
            DEBUG_STACK_FRAME_EX ScopeFrame,
            IntPtr ScopeContext,
            uint ScopeContextSize
        );

        [PreserveSig]
        new int GetNameByInlineContext(
            ulong Offset,
            uint InlineContext,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong Displacement
        );

        [PreserveSig]
        new int GetNameByInlineContextWide(
            ulong Offset,
            uint InlineContext,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong Displacement
        );

        [PreserveSig]
        new int GetLineByInlineContext(
            ulong Offset,
            uint InlineContext,
            out uint Line,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FileBuffer,
            int FileBufferSize,
            out uint FileSize,
            out ulong Displacement
        );

        [PreserveSig]
        new int GetLineByInlineContextWide(
            ulong Offset,
            uint InlineContext,
            out uint Line,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder FileBuffer,
            int FileBufferSize,
            out uint FileSize,
            out ulong Displacement
        );

        [PreserveSig]
        new int OutputSymbolByInlineContext(
            uint OutputControl,
            uint Flags,
            ulong Offset,
            uint InlineContext
        );

        /* IDebugSymbols5 */

        [PreserveSig]
        int GetCurrentScopeFrameIndexEx(
            DEBUG_FRAME Flags,
            out uint Index
        );

        [PreserveSig]
        int SetScopeFrameByIndexEx(
            DEBUG_FRAME Flags,
            uint Index
        );
    }
}