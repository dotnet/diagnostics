// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6b86fe2c-2c4f-4f0c-9da2-174311acc327")]
    public interface IDebugSystemObjects
    {
        [PreserveSig]
        int GetEventThread(
            out uint Id);

        [PreserveSig]
        int GetEventProcess(
            out uint Id);

        [PreserveSig]
        int GetCurrentThreadId(
            out uint Id);

        [PreserveSig]
        int SetCurrentThreadId(
            uint Id);

        [PreserveSig]
        int GetCurrentProcessId(
            out uint Id);

        [PreserveSig]
        int SetCurrentProcessId(
            uint Id);

        [PreserveSig]
        int GetNumberThreads(
            out uint Number);

        [PreserveSig]
        int GetTotalNumberThreads(
            out uint Total,
            out uint LargestProcess);

        [PreserveSig]
        int GetThreadIdsByIndex(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            uint[] Ids,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            uint[] SysIds);

        [PreserveSig]
        int GetThreadIdByProcessor(
            uint Processor,
            out uint Id);

        [PreserveSig]
        int GetCurrentThreadDataOffset(
            out ulong Offset);

        [PreserveSig]
        int GetThreadIdByDataOffset(
            ulong Offset,
            out uint Id);

        [PreserveSig]
        int GetCurrentThreadTeb(
            out ulong Offset);

        [PreserveSig]
        int GetThreadIdByTeb(
            ulong Offset,
            out uint Id);

        [PreserveSig]
        int GetCurrentThreadSystemId(
            out uint SysId);

        [PreserveSig]
        int GetThreadIdBySystemId(
            uint SysId,
            out uint Id);

        [PreserveSig]
        int GetCurrentThreadHandle(
            out ulong Handle);

        [PreserveSig]
        int GetThreadIdByHandle(
            ulong Handle,
            out uint Id);

        [PreserveSig]
        int GetNumberProcesses(
            out uint Number);

        [PreserveSig]
        int GetProcessIdsByIndex(
            uint Start,
            uint Count,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            uint[] Ids,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            uint[] SysIds);

        [PreserveSig]
        int GetCurrentProcessDataOffset(
            out ulong Offset);

        [PreserveSig]
        int GetProcessIdByDataOffset(
            ulong Offset,
            out uint Id);

        [PreserveSig]
        int GetCurrentProcessPeb(
            out ulong Offset);

        [PreserveSig]
        int GetProcessIdByPeb(
            ulong Offset,
            out uint Id);

        [PreserveSig]
        int GetCurrentProcessSystemId(
            out uint SysId);

        [PreserveSig]
        int GetProcessIdBySystemId(
            uint SysId,
            out uint Id);

        [PreserveSig]
        int GetCurrentProcessHandle(
            out ulong Handle);

        [PreserveSig]
        int GetProcessIdByHandle(
            ulong Handle,
            out uint Id);

        [PreserveSig]
        int GetCurrentProcessExecutableName(
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            int BufferSize,
            out uint ExeSize);
    }
}