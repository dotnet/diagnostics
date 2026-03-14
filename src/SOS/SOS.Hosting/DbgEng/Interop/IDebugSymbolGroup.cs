// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("f2528316-0f1a-4431-aeed-11d096e1e2ab")]
    public interface IDebugSymbolGroup
    {
        /* IDebugSymbolGroup */

        [PreserveSig]
        int GetNumberSymbols(
            out uint Number);

        [PreserveSig]
        int AddSymbol(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            ref uint Index);

        [PreserveSig]
        int RemoveSymbolByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name);

        [PreserveSig]
        int RemoveSymbolsByIndex(
            uint Index);

        [PreserveSig]
        int GetSymbolName(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint NameSize);

        [PreserveSig]
        int GetSymbolParameters(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_SYMBOL_PARAMETERS[] Params);

        [PreserveSig]
        int ExpandSymbol(
            uint Index,
            [In][MarshalAs(UnmanagedType.Bool)] bool Expand);

        [PreserveSig]
        int OutputSymbols(
            DEBUG_OUTCTL OutputControl,
            DEBUG_OUTPUT_SYMBOLS Flags,
            uint Start,
            uint Count);

        [PreserveSig]
        int WriteSymbol(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Value);

        [PreserveSig]
        int OutputAsType(
            uint Index,
            [In][MarshalAs(UnmanagedType.LPStr)] string Type);
    }
}
