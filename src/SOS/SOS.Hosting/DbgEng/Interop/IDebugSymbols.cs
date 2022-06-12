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
    [Guid("8c31e98c-983a-48a5-9016-6fe5d667a950")]
    public interface IDebugSymbols
    {
        /* IDebugSymbols */

        [PreserveSig]
        int GetSymbolOptions(
            out SYMOPT Options);

        [PreserveSig]
        int AddSymbolOptions(
            SYMOPT Options);

        [PreserveSig]
        int RemoveSymbolOptions(
            SYMOPT Options);

        [PreserveSig]
        int SetSymbolOptions(
            SYMOPT Options);

        [PreserveSig]
        int GetNameByOffset(
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong Displacement);

        [PreserveSig]
        int GetOffsetByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            out ulong Offset);

        [PreserveSig]
        int GetNearNameByOffset(
            ulong Offset,
            int Delta,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong Displacement);

        [PreserveSig]
        int GetLineByOffset(
            ulong Offset,
            out uint Line,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder FileBuffer,
            int FileBufferSize,
            out uint FileSize,
            out ulong Displacement);

        [PreserveSig]
        int GetOffsetByLine(
            uint Line,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            out ulong Offset);

        [PreserveSig]
        int GetNumberModules(
            out uint Loaded,
            out uint Unloaded);

        [PreserveSig]
        int GetModuleByIndex(
            uint Index,
            out ulong Base);

        [PreserveSig]
        int GetModuleByModuleName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            uint StartIndex,
            out uint Index,
            out ulong Base);

        [PreserveSig]
        int GetModuleByOffset(
            ulong Offset,
            uint StartIndex,
            out uint Index,
            out ulong Base);

        [PreserveSig]
        int GetModuleNames(
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
        int GetModuleParameters(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] ulong[] Bases,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_MODULE_PARAMETERS[] Params);

        [PreserveSig]
        int GetSymbolModule(
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            out ulong Base);

        [PreserveSig]
        int GetTypeName(
            ulong Module,
            uint TypeId,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetTypeId(
            ulong Module,
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            out uint TypeId);

        [PreserveSig]
        int GetTypeSize(
            ulong Module,
            uint TypeId,
            out uint Size);

        [PreserveSig]
        int GetFieldOffset(
            ulong Module,
            uint TypeId,
            [In][MarshalAs(UnmanagedType.LPStr)] string Field,
            out uint Offset);

        [PreserveSig]
        int GetSymbolTypeId(
            [In][MarshalAs(UnmanagedType.LPStr)] string Symbol,
            out uint TypeId,
            out ulong Module);

        [PreserveSig]
        int GetOffsetTypeId(
            ulong Offset,
            out uint TypeId,
            out ulong Module);

        [PreserveSig]
        int ReadTypedDataVirtual(
            ulong Offset,
            ulong Module,
            uint TypeId,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
            byte[] Buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteTypedDataVirtual(
            ulong Offset,
            ulong Module,
            uint TypeId,
            IntPtr Buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int OutputTypedDataVirtual(
            DEBUG_OUTCTL OutputControl,
            ulong Offset,
            ulong Module,
            uint TypeId,
            DEBUG_TYPEOPTS Flags);

        [PreserveSig]
        int ReadTypedDataPhysical(
            ulong Offset,
            ulong Module,
            uint TypeId,
            IntPtr Buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteTypedDataPhysical(
            ulong Offset,
            ulong Module,
            uint TypeId,
            IntPtr Buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int OutputTypedDataPhysical(
            DEBUG_OUTCTL OutputControl,
            ulong Offset,
            ulong Module,
            uint TypeId,
            DEBUG_TYPEOPTS Flags);

        [PreserveSig]
        int GetScope(
            out ulong InstructionOffset,
            out DEBUG_STACK_FRAME ScopeFrame,
            IntPtr ScopeContext,
            uint ScopeContextSize);

        [PreserveSig]
        int SetScope(
            ulong InstructionOffset,
            in DEBUG_STACK_FRAME ScopeFrame,
            IntPtr ScopeContext,
            uint ScopeContextSize);

        [PreserveSig]
        int ResetScope();

        [PreserveSig]
        int GetScopeSymbolGroup(
            DEBUG_SCOPE_GROUP Flags,
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugSymbolGroup Update,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugSymbolGroup Symbols);

        [PreserveSig]
        int CreateSymbolGroup(
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugSymbolGroup Group);

        [PreserveSig]
        int StartSymbolMatch(
            [In][MarshalAs(UnmanagedType.LPStr)] string Pattern,
            out ulong Handle);

        [PreserveSig]
        int GetNextSymbolMatch(
            ulong Handle,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint MatchSize,
            out ulong Offset);

        [PreserveSig]
        int EndSymbolMatch(
            ulong Handle);

        [PreserveSig]
        int Reload(
            [In][MarshalAs(UnmanagedType.LPStr)] string Module);

        [PreserveSig]
        int GetSymbolPath(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        int SetSymbolPath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [PreserveSig]
        int AppendSymbolPath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [PreserveSig]
        int GetImagePath(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        int SetImagePath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [PreserveSig]
        int AppendImagePath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [PreserveSig]
        int GetSourcePath(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PathSize);

        [PreserveSig]
        int GetSourcePathElement(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint ElementSize);

        [PreserveSig]
        int SetSourcePath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Path);

        [PreserveSig]
        int AppendSourcePath(
            [In][MarshalAs(UnmanagedType.LPStr)] string Addition);

        [PreserveSig]
        int FindSourceFile(
            uint StartElement,
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            DEBUG_FIND_SOURCE Flags,
            out uint FoundElement,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint FoundSize);

        [PreserveSig]
        int GetSourceFileLineOffsets(
            [In][MarshalAs(UnmanagedType.LPStr)] string File,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            ulong[] Buffer,
            int BufferLines,
            out uint FileLines);
    }
}