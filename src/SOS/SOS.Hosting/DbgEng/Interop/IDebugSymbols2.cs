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
    [Guid("3a707211-afdd-4495-ad4f-56fecdf8163f")]
    public interface IDebugSymbols2 : IDebugSymbols
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
        int GetModuleVersionInformation(
            uint Index,
            ulong Base,
            [In][MarshalAs(UnmanagedType.LPStr)] string Item,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
            byte[] buffer,
            uint BufferSize,
            out uint VerInfoSize);

        [PreserveSig]
        int GetModuleNameString(
            DEBUG_MODNAME Which,
            uint Index,
            ulong Base,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            uint BufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetConstantName(
            ulong Module,
            uint TypeId,
            ulong Value,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetFieldName(
            ulong Module,
            uint TypeId,
            uint FieldIndex,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetTypeOptions(
            out DEBUG_TYPEOPTS Options);

        [PreserveSig]
        int AddTypeOptions(
            DEBUG_TYPEOPTS Options);

        [PreserveSig]
        int RemoveTypeOptions(
            DEBUG_TYPEOPTS Options);

        [PreserveSig]
        int SetTypeOptions(
            DEBUG_TYPEOPTS Options);
    }
}
