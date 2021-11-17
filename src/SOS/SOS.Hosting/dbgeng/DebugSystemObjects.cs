// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting
{
    internal unsafe class DebugSystemObjects
    {
        internal DebugSystemObjects(DebugClient client, SOSHost soshost)
        {
            VTableBuilder builder = client.AddInterface(typeof(IDebugSystemObjects).GUID, validate: true);
            AddDebugSystemObjects(builder, soshost);
            builder.Complete();
        }

        private static void AddDebugSystemObjects(VTableBuilder builder, SOSHost soshost)
        {
            builder.AddMethod(new GetEventThreadDelegate((self, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetEventProcessDelegate((self, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentThreadIdDelegate(soshost.GetCurrentThreadId));
            builder.AddMethod(new SetCurrentThreadIdDelegate(soshost.SetCurrentThreadId));
            builder.AddMethod(new GetCurrentProcessIdDelegate((self, id) => DebugClient.NotImplemented));
            builder.AddMethod(new SetCurrentProcessIdDelegate((self, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNumberThreadsDelegate(soshost.GetNumberThreads));
            builder.AddMethod(new GetTotalNumberThreadsDelegate(soshost.GetTotalNumberThreads));
            builder.AddMethod(new GetThreadIdsByIndexDelegate(soshost.GetThreadIdsByIndex));
            builder.AddMethod(new GetThreadIdByProcessorDelegate((self, processor, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentThreadDataOffsetDelegate((self, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetThreadIdByDataOffsetDelegate((self, offset, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentThreadTebDelegate(soshost.GetCurrentThreadTeb));
            builder.AddMethod(new GetThreadIdByTebDelegate((self, offset, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentThreadSystemIdDelegate(soshost.GetCurrentThreadSystemId));
            builder.AddMethod(new GetThreadIdBySystemIdDelegate(soshost.GetThreadIdBySystemId));
            builder.AddMethod(new GetCurrentThreadHandleDelegate((self, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new GetThreadIdByHandleDelegate((self, handle, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetNumberProcessesDelegate((self, number) => DebugClient.NotImplemented));
            builder.AddMethod(new GetProcessIdsByIndexDelegate((self, start, count, ids, sysIds) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentProcessDataOffsetDelegate((self, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetProcessIdByDataOffsetDelegate((self, offset, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentProcessPebDelegate((self, offset) => DebugClient.NotImplemented));
            builder.AddMethod(new GetProcessIdByPebDelegate((self, offset, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentProcessSystemIdDelegate(soshost.GetCurrentProcessSystemId));
            builder.AddMethod(new GetProcessIdBySystemIdDelegate((self, sysId, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentProcessHandleDelegate((self, handle) => DebugClient.NotImplemented));
            builder.AddMethod(new GetProcessIdByHandleDelegate((self, handle, id) => DebugClient.NotImplemented));
            builder.AddMethod(new GetCurrentProcessExecutableNameDelegate((self, buffer, bufferSize, exeSize) => DebugClient.NotImplemented));
        }

        #region IDebugSystemObjects Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEventThreadDelegate(
            IntPtr self,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEventProcessDelegate(
            IntPtr self,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentThreadIdDelegate(
            IntPtr self,
            [Out] out uint Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetCurrentThreadIdDelegate(
            IntPtr self,
            [In] uint Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentProcessIdDelegate(
            IntPtr self,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetCurrentProcessIdDelegate(
            IntPtr self,
            [In] uint Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberThreadsDelegate(
            IntPtr self,
            [Out] out uint Number);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetTotalNumberThreadsDelegate(
            IntPtr self,
            [Out] out uint Total,
            [Out] out uint LargestProcess);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadIdsByIndexDelegate(
            IntPtr self,
            [In] uint Start,
            [In] uint Count,
            [Out] uint* Ids,
            [Out] uint* SysIds);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadIdByProcessorDelegate(
            IntPtr self,
            [In] uint Processor,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentThreadDataOffsetDelegate(
            IntPtr self,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadIdByDataOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentThreadTebDelegate(
            IntPtr self,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadIdByTebDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentThreadSystemIdDelegate(
            IntPtr self,
            [Out] out uint SysId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadIdBySystemIdDelegate(
            IntPtr self,
            [In] uint SysId,
            [Out] out uint Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentThreadHandleDelegate(
            IntPtr self,
            [Out] ulong* Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetThreadIdByHandleDelegate(
            IntPtr self,
            [In] ulong Handle,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberProcessesDelegate(
            IntPtr self,
            [Out] uint* Number);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProcessIdsByIndexDelegate(
            IntPtr self,
            [In] uint Start,
            [In] uint Count,
            [Out] uint* Ids,
            [Out] uint* SysIds);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentProcessDataOffsetDelegate(
            IntPtr self,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProcessIdByDataOffsetDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentProcessPebDelegate(
            IntPtr self,
            [Out] ulong* Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProcessIdByPebDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentProcessSystemIdDelegate(
            IntPtr self,
            [Out] out uint SysId);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProcessIdBySystemIdDelegate(
            IntPtr self,
            [In] uint SysId,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentProcessHandleDelegate(
            IntPtr self,
            [Out] ulong* Handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProcessIdByHandleDelegate(
            IntPtr self,
            [In] ulong Handle,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCurrentProcessExecutableNameDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* ExeSize);

        #endregion
    }
}