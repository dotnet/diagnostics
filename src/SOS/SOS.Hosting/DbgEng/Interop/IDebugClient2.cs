// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("edbed635-372e-4dab-bbfe-ed0d2f63be81")]
    public interface IDebugClient2 : IDebugClient
    {
        /* IDebugClient */

        [PreserveSig]
        new int AttachKernel(
            DEBUG_ATTACH Flags,
            [In][MarshalAs(UnmanagedType.LPStr)] string ConnectOptions);

        [PreserveSig]
        new int GetKernelConnectionOptions(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint OptionsSize);

        [PreserveSig]
        new int SetKernelConnectionOptions(
            [In][MarshalAs(UnmanagedType.LPStr)] string Options);

        [PreserveSig]
        new int StartProcessServer(
            DEBUG_CLASS Flags,
            [In][MarshalAs(UnmanagedType.LPStr)] string Options,
            IntPtr Reserved);

        [PreserveSig]
        new int ConnectProcessServer(
            [In][MarshalAs(UnmanagedType.LPStr)] string RemoteOptions,
            out ulong Server);

        [PreserveSig]
        new int DisconnectProcessServer(
            ulong Server);

        [PreserveSig]
        new int GetRunningProcessSystemIds(
            ulong Server,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            uint[] Ids,
            uint Count,
            out uint ActualCount);

        [PreserveSig]
        new int GetRunningProcessSystemIdByExecutableName(
            ulong Server,
            [In][MarshalAs(UnmanagedType.LPStr)] string ExeName,
            DEBUG_GET_PROC Flags,
            out uint Id);

        [PreserveSig]
        new int GetRunningProcessDescription(
            ulong Server,
            uint SystemId,
            DEBUG_PROC_DESC Flags,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder ExeName,
            int ExeNameSize,
            out uint ActualExeNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Description,
            int DescriptionSize,
            out uint ActualDescriptionSize);

        [PreserveSig]
        new int AttachProcess(
            ulong Server,
            uint ProcessID,
            DEBUG_ATTACH AttachFlags);

        [PreserveSig]
        new int CreateProcess(
            ulong Server,
            [In][MarshalAs(UnmanagedType.LPStr)] string CommandLine,
            DEBUG_CREATE_PROCESS Flags);

        [PreserveSig]
        new int CreateProcessAndAttach(
            ulong Server,
            [In][MarshalAs(UnmanagedType.LPStr)] string CommandLine,
            DEBUG_CREATE_PROCESS Flags,
            uint ProcessId,
            DEBUG_ATTACH AttachFlags);

        [PreserveSig]
        new int GetProcessOptions(
            out DEBUG_PROCESS Options);

        [PreserveSig]
        new int AddProcessOptions(
            DEBUG_PROCESS Options);

        [PreserveSig]
        new int RemoveProcessOptions(
            DEBUG_PROCESS Options);

        [PreserveSig]
        new int SetProcessOptions(
            DEBUG_PROCESS Options);

        [PreserveSig]
        new int OpenDumpFile(
            [In][MarshalAs(UnmanagedType.LPStr)] string DumpFile);

        [PreserveSig]
        new int WriteDumpFile(
            [In][MarshalAs(UnmanagedType.LPStr)] string DumpFile,
            DEBUG_DUMP Qualifier);

        [PreserveSig]
        new int ConnectSession(
            DEBUG_CONNECT_SESSION Flags,
            uint HistoryLimit);

        [PreserveSig]
        new int StartServer(
            [In][MarshalAs(UnmanagedType.LPStr)] string Options);

        [PreserveSig]
        new int OutputServer(
            DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Machine,
            DEBUG_SERVERS Flags);

        [PreserveSig]
        new int TerminateProcesses();

        [PreserveSig]
        new int DetachProcesses();

        [PreserveSig]
        new int EndSession(
            DEBUG_END Flags);

        [PreserveSig]
        new int GetExitCode(
            out uint Code);

        [PreserveSig]
        new int DispatchCallbacks(
            uint Timeout);

        [PreserveSig]
        new int ExitDispatch(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugClient Client);

        [PreserveSig]
        new int CreateClient(
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugClient Client);

        [PreserveSig]
        new int GetInputCallbacks(
            [Out][MarshalAs(UnmanagedType.Interface)]
            out IDebugInputCallbacks Callbacks);

        [PreserveSig]
        new int SetInputCallbacks(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugInputCallbacks Callbacks);

        /* GetOutputCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */

        [PreserveSig]
        new int GetOutputCallbacks(
            out IDebugOutputCallbacks Callbacks);

        /* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */

        [PreserveSig]
        new int SetOutputCallbacks(
            [In] IDebugOutputCallbacks Callbacks);

        [PreserveSig]
        new int GetOutputMask(
            out DEBUG_OUTPUT Mask);

        [PreserveSig]
        new int SetOutputMask(
            DEBUG_OUTPUT Mask);

        [PreserveSig]
        new int GetOtherOutputMask(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugClient Client,
            out DEBUG_OUTPUT Mask);

        [PreserveSig]
        new int SetOtherOutputMask(
            [In][MarshalAs(UnmanagedType.Interface)]
            IDebugClient Client,
            DEBUG_OUTPUT Mask);

        [PreserveSig]
        new int GetOutputWidth(
            out uint Columns);

        [PreserveSig]
        new int SetOutputWidth(
            uint Columns);

        [PreserveSig]
        new int GetOutputLinePrefix(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint PrefixSize);

        [PreserveSig]
        new int SetOutputLinePrefix(
            [In][MarshalAs(UnmanagedType.LPStr)] string Prefix);

        [PreserveSig]
        new int GetIdentity(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint IdentitySize);

        [PreserveSig]
        new int OutputIdentity(
            DEBUG_OUTCTL OutputControl,
            uint Flags,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        /* GetEventCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */

        [PreserveSig]
        new int GetEventCallbacks(
            out IDebugEventCallbacks Callbacks);

        /* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */

        [PreserveSig]
        new int SetEventCallbacks(
            [In] IDebugEventCallbacks Callbacks);

        [PreserveSig]
        new int FlushCallbacks();

        /* IDebugClient2 */

        [PreserveSig]
        int WriteDumpFile2(
            [In][MarshalAs(UnmanagedType.LPStr)] string DumpFile,
            DEBUG_DUMP Qualifier,
            DEBUG_FORMAT FormatFlags,
            [In][MarshalAs(UnmanagedType.LPStr)] string Comment);

        [PreserveSig]
        int AddDumpInformationFile(
            [In][MarshalAs(UnmanagedType.LPStr)] string InfoFile,
            DEBUG_DUMP_FILE Type);

        [PreserveSig]
        int EndProcessServer(
            ulong Server);

        [PreserveSig]
        int WaitForProcessServerEnd(
            uint Timeout);

        [PreserveSig]
        int IsKernelDebuggerEnabled();

        [PreserveSig]
        int TerminateCurrentProcess();

        [PreserveSig]
        int DetachCurrentProcess();

        [PreserveSig]
        int AbandonCurrentProcess();
    }
}
