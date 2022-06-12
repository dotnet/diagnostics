// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0690e046-9c23-45ac-a04f-987ac29ad0d3")]
    public interface IDebugEventCallbacksWide
    {
        [PreserveSig]
        int GetInterestMask(
            out DEBUG_EVENT Mask);

        [PreserveSig]
        int Breakpoint(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugBreakpoint2 Bp);

        [PreserveSig]
        int Exception(
            in EXCEPTION_RECORD64 Exception,
            uint FirstChance);

        [PreserveSig]
        int CreateThread(
            ulong Handle,
            ulong DataOffset,
            ulong StartOffset);

        [PreserveSig]
        int ExitThread(
            uint ExitCode);

        [PreserveSig]
        int CreateProcess(
            ulong ImageFileHandle,
            ulong Handle,
            ulong BaseOffset,
            uint ModuleSize,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ModuleName,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ImageName,
            uint CheckSum,
            uint TimeDateStamp,
            ulong InitialThreadHandle,
            ulong ThreadDataOffset,
            ulong StartOffset);

        [PreserveSig]
        int ExitProcess(
            uint ExitCode);

        [PreserveSig]
        int LoadModule(
            ulong ImageFileHandle,
            ulong BaseOffset,
            uint ModuleSize,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ModuleName,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ImageName,
            uint CheckSum,
            uint TimeDateStamp);

        [PreserveSig]
        int UnloadModule(
            [In][MarshalAs(UnmanagedType.LPWStr)] string ImageBaseName,
            ulong BaseOffset);

        [PreserveSig]
        int SystemError(
            uint Error,
            uint Level);

        [PreserveSig]
        int SessionStatus(
            DEBUG_SESSION Status);

        [PreserveSig]
        int ChangeDebuggeeState(
            DEBUG_CDS Flags,
            ulong Argument);

        [PreserveSig]
        int ChangeEngineState(
            DEBUG_CES Flags,
            ulong Argument);

        [PreserveSig]
        int ChangeSymbolState(
            DEBUG_CSS Flags,
            ulong Argument);
    }
}