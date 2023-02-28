// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("5bd9d474-5975-423a-b88b-65a8e7110e65")]
    public interface IDebugBreakpoint
    {
        /* IDebugBreakpoint */

        [PreserveSig]
        int GetId(
            out uint Id);

        [PreserveSig]
        int GetType(
            out DEBUG_BREAKPOINT_TYPE BreakType,
            out uint ProcType);

        //FIX ME!!! Should try and get an enum for this
        [PreserveSig]
        int GetAdder(
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugClient Adder);

        [PreserveSig]
        int GetFlags(
            out DEBUG_BREAKPOINT_FLAG Flags);

        [PreserveSig]
        int AddFlags(
            DEBUG_BREAKPOINT_FLAG Flags);

        [PreserveSig]
        int RemoveFlags(
            DEBUG_BREAKPOINT_FLAG Flags);

        [PreserveSig]
        int SetFlags(
            DEBUG_BREAKPOINT_FLAG Flags);

        [PreserveSig]
        int GetOffset(
            out ulong Offset);

        [PreserveSig]
        int SetOffset(
            ulong Offset);

        [PreserveSig]
        int GetDataParameters(
            out uint Size,
            out DEBUG_BREAKPOINT_ACCESS_TYPE AccessType);

        [PreserveSig]
        int SetDataParameters(
            uint Size,
            DEBUG_BREAKPOINT_ACCESS_TYPE AccessType);

        [PreserveSig]
        int GetPassCount(
            out uint Count);

        [PreserveSig]
        int SetPassCount(
            uint Count);

        [PreserveSig]
        int GetCurrentPassCount(
            out uint Count);

        [PreserveSig]
        int GetMatchThreadId(
            out uint Id);

        [PreserveSig]
        int SetMatchThreadId(
            uint Thread);

        [PreserveSig]
        int GetCommand(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint CommandSize);

        [PreserveSig]
        int SetCommand(
            [In][MarshalAs(UnmanagedType.LPStr)] string Command);

        [PreserveSig]
        int GetOffsetExpression(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint ExpressionSize);

        [PreserveSig]
        int SetOffsetExpression(
            [In][MarshalAs(UnmanagedType.LPStr)] string Expression);

        [PreserveSig]
        int GetParameters(
            out DEBUG_BREAKPOINT_PARAMETERS Params);
    }
}
