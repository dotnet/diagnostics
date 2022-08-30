// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6a7ccc5f-fb5e-4dcc-b41c-6c20307bccc7")]
    public interface IDebugSymbolGroup2 : IDebugSymbolGroup
    {
        /* IDebugSymbolGroup */

        [PreserveSig]
        new int GetNumberSymbols(
            out uint Number);

        [PreserveSig]
        new int AddSymbol(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            ref uint Index);

        [PreserveSig]
        new int RemoveSymbolByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name);

        [PreserveSig]
        new int RemoveSymbolsByIndex(
            uint Index);

        [PreserveSig]
        new int GetSymbolName(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        new int GetSymbolParameters(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_SYMBOL_PARAMETERS[] Params);

        [PreserveSig]
        new int ExpandSymbol(
            uint Index,
            [In][MarshalAs(UnmanagedType.Bool)] bool Expand);

        [PreserveSig]
        new int OutputSymbols(
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUTPUT_SYMBOLS Flags,
            uint Start,
            uint Count);

        [PreserveSig]
        new int WriteSymbol(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Value);

        [PreserveSig]
        new int OutputAsType(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Type);

        /* IDebugSymbolGroup2 */

        [PreserveSig]
        int AddSymbolWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            ref uint Index);

        [PreserveSig]
        int RemoveSymbolByNameWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name);

        [PreserveSig]
        int GetSymbolNameWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        int WriteSymbolWide(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Value);

        [PreserveSig]
        int OutputAsTypeWide(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPWStr)] string Type);

        [PreserveSig]
        int GetSymbolTypeName(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetSymbolTypeNameWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetSymbolSize(
            uint Index,
            out uint Size);

        [PreserveSig]
        int GetSymbolOffset(
            uint Index,
            out ulong Offset);

        [PreserveSig]
        int GetSymbolRegister(
            uint Index,
            out uint Register);

        [PreserveSig]
        int GetSymbolValueText(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetSymbolValueTextWide(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetSymbolEntryInformation(
            uint Index,
            out DEBUG_SYMBOL_ENTRY Info);
    }
}