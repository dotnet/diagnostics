// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("ce289126-9e84-45a7-937e-67bb18691493")]
    public interface IDebugRegisters
    {
        [PreserveSig]
        int GetNumberRegisters(
            out uint Number);

        [PreserveSig]
        int GetDescription(
            uint Register,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out DEBUG_REGISTER_DESCRIPTION Desc);

        [PreserveSig]
        int GetIndexByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            out uint Index);

        [PreserveSig]
        int GetValue(
            uint Register,
            out DEBUG_VALUE Value);

        [PreserveSig]
        int SetValue(
            uint Register,
            in DEBUG_VALUE Value);

        [PreserveSig]
        int GetValues( //FIX ME!!! This needs to be tested
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Indices,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_VALUE[] Values);

        [PreserveSig]
        int SetValues(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Indices,
            uint Start,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] Values);

        [PreserveSig]
        int OutputRegisters(
            DEBUG_OUTCTL OutputControl,
            DEBUG_REGISTERS Flags);

        [PreserveSig]
        int GetInstructionOffset(
            out ulong Offset);

        [PreserveSig]
        int GetStackOffset(
            out ulong Offset);

        [PreserveSig]
        int GetFrameOffset(
            out ulong Offset);
    }
}
