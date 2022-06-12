// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("61a4905b-23f9-4247-b3c5-53d087529ab7")]
    public unsafe interface IDebugEventContextCallbacks
    {
        [PreserveSig]
        int GetInterestMask(
            out DEBUG_EVENT Mask);

        [PreserveSig]
        int Breakpoint(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugBreakpoint2 Bp,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int Exception(
            in EXCEPTION_RECORD64 Exception,
            uint FirstChance,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int CreateThread(
            ulong Handle,
            ulong DataOffset,
            ulong StartOffset,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int ExitThread(
            uint ExitCode,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

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
            ulong StartOffset,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int ExitProcess(
            uint ExitCode,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int LoadModule(
            ulong ImageFileHandle,
            ulong BaseOffset,
            uint ModuleSize,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ModuleName,
            [In][MarshalAs(UnmanagedType.LPWStr)] string ImageName,
            uint CheckSum,
            uint TimeDateStamp,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int UnloadModule(
            [In][MarshalAs(UnmanagedType.LPWStr)] string ImageBaseName,
            ulong BaseOffset,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int SystemError(
            uint Error,
            uint Level,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int SessionStatus(
            DEBUG_SESSION Status);

        [PreserveSig]
        int ChangeDebuggeeState(
            DEBUG_CDS Flags,
            ulong Argument,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int ChangeEngineState(
            DEBUG_CES Flags,
            ulong Argument,
            in DEBUG_EVENT_CONTEXT Context,
            uint ContextSize);

        [PreserveSig]
        int ChangeSymbolState(
            DEBUG_CSS Flags,
            ulong Argument);
    }
}