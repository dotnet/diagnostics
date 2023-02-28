// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("1656afa9-19c6-4e3a-97e7-5dc9160cf9c4")]
    public interface IDebugRegisters2 : IDebugRegisters
    {
        [PreserveSig]
        new int GetNumberRegisters(
            out uint Number);

        [PreserveSig]
        new int GetDescription(
            uint Register,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out DEBUG_REGISTER_DESCRIPTION Desc);

        [PreserveSig]
        new int GetIndexByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            out uint Index);

        [PreserveSig]
        new int GetValue(
            uint Register,
            out DEBUG_VALUE Value);

        [PreserveSig]
        new int SetValue(
            uint Register,
            in DEBUG_VALUE Value);

        [PreserveSig]
        new int GetValues( //FIX ME!!! This needs to be tested
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Indices,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_VALUE[] Values);

        [PreserveSig]
        new int SetValues(
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Indices,
            uint Start,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] Values);

        [PreserveSig]
        new int OutputRegisters(
            DEBUG_OUTCTL OutputControl,
            DEBUG_REGISTERS Flags);

        [PreserveSig]
        new int GetInstructionOffset(
            out ulong Offset);

        [PreserveSig]
        new int GetStackOffset(
            out ulong Offset);

        [PreserveSig]
        new int GetFrameOffset(
            out ulong Offset);

        /* IDebugRegisters2 */

        [PreserveSig]
        int GetDescriptionWide(
            uint Register,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out DEBUG_REGISTER_DESCRIPTION Desc);

        [PreserveSig]
        int GetIndexByNameWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            out uint Index);

        [PreserveSig]
        int GetNumberPseudoRegisters(
            out uint Number
        );

        [PreserveSig]
        int GetPseudoDescription(
            uint Register,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong TypeModule,
            out uint TypeId
        );

        [PreserveSig]
        int GetPseudoDescriptionWide(
            uint Register,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder NameBuffer,
            int NameBufferSize,
            out uint NameSize,
            out ulong TypeModule,
            out uint TypeId
        );

        [PreserveSig]
        int GetPseudoIndexByName(
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            out uint Index
        );

        [PreserveSig]
        int GetPseudoIndexByNameWide(
            [In][MarshalAs(UnmanagedType.LPWStr)] string Name,
            out uint Index
        );

        [PreserveSig]
        int GetPseudoValues(
            uint Source,
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Indices,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_VALUE[] Values
        );

        [PreserveSig]
        int SetPseudoValues(
            uint Source,
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Indices,
            uint Start,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] Values
        );

        [PreserveSig]
        int GetValues2(
            DEBUG_REGSRC Source,
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Indices,
            uint Start,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            DEBUG_VALUE[] Values
        );

        [PreserveSig]
        int SetValues2(
            uint Source,
            uint Count,
            [In][MarshalAs(UnmanagedType.LPArray)] uint[] Indices,
            uint Start,
            [In][MarshalAs(UnmanagedType.LPArray)] DEBUG_VALUE[] Values
        );

        [PreserveSig]
        int OutputRegisters2(
            uint OutputControl,
            uint Source,
            uint Flags
        );

        [PreserveSig]
        int GetInstructionOffset2(
            uint Source,
            out ulong Offset
        );

        [PreserveSig]
        int GetStackOffset2(
            uint Source,
            out ulong Offset
        );

        [PreserveSig]
        int GetFrameOffset2(
            uint Source,
            out ulong Offset
        );
    }
}
