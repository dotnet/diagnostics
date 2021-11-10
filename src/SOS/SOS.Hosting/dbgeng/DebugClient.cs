// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting
{
    internal unsafe class DebugClient : COMCallableIUnknown
    {
        internal readonly IntPtr IDebugClient;

        private readonly DebugAdvanced _debugAdvanced;
        private readonly DebugControl _debugControl;
        private readonly DebugDataSpaces _debugDataSpaces;
        private readonly DebugRegisters _debugRegisters;
        private readonly DebugSymbols _debugSymbols;
        private readonly DebugSystemObjects _debugSystemObjects;

        /// <summary>
        /// Create an instance of the service wrapper SOS uses.
        /// </summary>
        /// <param name="soshost">SOS host instance</param>
        public DebugClient(SOSHost soshost)
        {
            VTableBuilder builder = AddInterface(typeof(IDebugClient).GUID, validate: true);
            AddDebugClient(builder, soshost);
            IDebugClient = builder.Complete();

            _debugAdvanced = new DebugAdvanced(this, soshost);
            _debugControl = new DebugControl(this, soshost);
            _debugDataSpaces = new DebugDataSpaces(this, soshost);
            _debugRegisters = new DebugRegisters(this, soshost);
            _debugSymbols = new DebugSymbols(this, soshost);
            _debugSystemObjects = new DebugSystemObjects(this, soshost);

            AddRef();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("DebugClient.Destroy");
        }

        private static void AddDebugClient(VTableBuilder builder, SOSHost soshost)
        {
            builder.AddMethod(new AttachKernelDelegate((self, flags, connectOptions) => NotImplemented));
            builder.AddMethod(new GetKernelConnectionOptionsDelegate((self, buffer, bufferSize, optionsSize) => NotImplemented));
            builder.AddMethod(new SetKernelConnectionOptionsDelegate((self, options) => NotImplemented));
            builder.AddMethod(new StartProcessServerDelegate((self, flags, options, reserved) => NotImplemented));
            builder.AddMethod(new ConnectProcessServerDelegate((self, remoteOptions, server) => NotImplemented));
            builder.AddMethod(new DisconnectProcessServerDelegate((self, server) => NotImplemented));
            builder.AddMethod(new GetRunningProcessSystemIdsDelegate((self, server, ids, count, actualCount) => NotImplemented));
            builder.AddMethod(new GetRunningProcessSystemIdByExecutableNameDelegate((self, server, exeName, flags, id) => NotImplemented));
            builder.AddMethod(new GetRunningProcessDescriptionDelegate((self, server, systemId, flags, exeName, exeNameSize, actualExeNameSize, description, descriptionSize, actualDescriptionSize) => NotImplemented));
            builder.AddMethod(new AttachProcessDelegate((self, server, processId, attachFlags) => NotImplemented));
            builder.AddMethod(new CreateProcessDelegate((self, server, commandLine, flags) => NotImplemented));
            builder.AddMethod(new CreateProcessAndAttachDelegate((self, server, commandLine, flags, processId, attachFlags) => NotImplemented));
            builder.AddMethod(new GetProcessOptionsDelegate((self, options) => NotImplemented));
            builder.AddMethod(new AddProcessOptionsDelegate((self, options) => NotImplemented));
            builder.AddMethod(new RemoveProcessOptionsDelegate((self, options) => NotImplemented));
            builder.AddMethod(new SetProcessOptionsDelegate((self, options) => NotImplemented));
            builder.AddMethod(new OpenDumpFileDelegate((self, dumpFile) => NotImplemented));
            builder.AddMethod(new WriteDumpFileDelegate((self, dumpFile, qualifier) => NotImplemented));
            builder.AddMethod(new ConnectSessionDelegate((self, flags, historyLimit) => NotImplemented));
            builder.AddMethod(new StartServerDelegate((self, options) => NotImplemented));
            builder.AddMethod(new OutputServerDelegate((self, outputControl, machine, flags) => NotImplemented));
            builder.AddMethod(new TerminateProcessesDelegate((self) => NotImplemented));
            builder.AddMethod(new DetachProcessesDelegate((self) => NotImplemented));
            builder.AddMethod(new EndSessionDelegate((self, flags) => NotImplemented));
            builder.AddMethod(new GetExitCodeDelegate((self, code) => NotImplemented));
            builder.AddMethod(new DispatchCallbacksDelegate((self, timeout) => NotImplemented));
            builder.AddMethod(new ExitDispatchDelegate((self, client) => NotImplemented));
            builder.AddMethod(new CreateClientDelegate((self, client) => NotImplemented));
            builder.AddMethod(new GetInputCallbacksDelegate((self, callbacks) => NotImplemented));
            builder.AddMethod(new SetInputCallbacksDelegate((self, callbacks) => NotImplemented));
            builder.AddMethod(new GetOutputCallbacksDelegate((self, callbacks) => NotImplemented));
            builder.AddMethod(new SetOutputCallbacksDelegate((self, callbacks) => NotImplemented));
            builder.AddMethod(new GetOutputMaskDelegate((self, mask) => NotImplemented));
            builder.AddMethod(new SetOutputMaskDelegate((self, mask) => NotImplemented));
            builder.AddMethod(new GetOtherOutputMaskDelegate((self, client, mask) => NotImplemented));
            builder.AddMethod(new SetOtherOutputMaskDelegate((self, client, mask) => NotImplemented));
            builder.AddMethod(new GetOutputWidthDelegate((self, columns) => NotImplemented));
            builder.AddMethod(new SetOutputWidthDelegate((self, columns) => NotImplemented));
            builder.AddMethod(new GetOutputLinePrefixDelegate((self, buffer, bufferSize, prefixSize) => NotImplemented));
            builder.AddMethod(new SetOutputLinePrefixDelegate((self, prefix) => NotImplemented));
            builder.AddMethod(new GetIdentityDelegate((self, buffer, bufferSize, identitySize) => NotImplemented));
            builder.AddMethod(new OutputIdentityDelegate((self, outputControl, flags, format) => NotImplemented));
            builder.AddMethod(new GetEventCallbacksDelegate((self, callbacks) => NotImplemented));
            builder.AddMethod(new SetEventCallbacksDelegate((self, callbacks) => NotImplemented));
            builder.AddMethod(new FlushCallbacksDelegate((self) => NotImplemented));
        }

        internal static int NotImplemented
        {
            get
            {
                System.Diagnostics.Debugger.Break();
                return HResult.E_NOTIMPL;
            }
        }

        #region IDebugClient Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AttachKernelDelegate(
            IntPtr self,
            [In] DEBUG_ATTACH flags,
            [In][MarshalAs(UnmanagedType.LPStr)] string connectOptions);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetKernelConnectionOptionsDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* OptionsSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetKernelConnectionOptionsDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int StartProcessServerDelegate(
            IntPtr self,
            [In] DEBUG_CLASS Flags,
            [In][MarshalAs(UnmanagedType.LPStr)] string Options,
            [In] IntPtr Reserved);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ConnectProcessServerDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string RemoteOptions,
            [Out] ulong* Server);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DisconnectProcessServerDelegate(
            IntPtr self,
            [In] ulong Server);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetRunningProcessSystemIdsDelegate(
            IntPtr self,
            [In] ulong Server,
            [Out] uint* Ids,
            [In] uint Count,
            [Out] uint* ActualCount);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetRunningProcessSystemIdByExecutableNameDelegate(
            IntPtr self,
            [In] ulong Server,
            [In][MarshalAs(UnmanagedType.LPStr)] string ExeName,
            [In] DEBUG_GET_PROC Flags,
            [Out] uint* Id);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetRunningProcessDescriptionDelegate(
            IntPtr self,
            [In] ulong Server,
            [In] uint SystemId,
            [In] DEBUG_PROC_DESC Flags,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder ExeName,
            [In] int ExeNameSize,
            [Out] uint* ActualExeNameSize,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Description,
            [In] int DescriptionSize,
            [Out] uint* ActualDescriptionSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AttachProcessDelegate(
            IntPtr self,
            [In] ulong Server,
            [In] uint ProcessID,
            [In] DEBUG_ATTACH AttachFlags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CreateProcessDelegate(
            IntPtr self,
            [In] ulong Server,
            [In][MarshalAs(UnmanagedType.LPStr)] string CommandLine,
            [In] DEBUG_CREATE_PROCESS Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CreateProcessAndAttachDelegate(
            IntPtr self,
            [In] ulong Server,
            [In][MarshalAs(UnmanagedType.LPStr)] string CommandLine,
            [In] DEBUG_CREATE_PROCESS Flags,
            [In] uint ProcessId,
            [In] DEBUG_ATTACH AttachFlags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetProcessOptionsDelegate(
            IntPtr self,
            [Out] DEBUG_PROCESS* Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int AddProcessOptionsDelegate(
            IntPtr self,
            [In] DEBUG_PROCESS Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int RemoveProcessOptionsDelegate(
            IntPtr self,
            [In] DEBUG_PROCESS Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetProcessOptionsDelegate(
            IntPtr self,
            [In] DEBUG_PROCESS Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OpenDumpFileDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string DumpFile);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteDumpFileDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string DumpFile,
            [In] DEBUG_DUMP Qualifier);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ConnectSessionDelegate(
            IntPtr self,
            [In] DEBUG_CONNECT_SESSION Flags,
            [In] uint HistoryLimit);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int StartServerDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Options);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputServerDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In][MarshalAs(UnmanagedType.LPStr)] string Machine,
            [In] DEBUG_SERVERS Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int TerminateProcessesDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DetachProcessesDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int EndSessionDelegate(
            IntPtr self,
            [In] DEBUG_END Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetExitCodeDelegate(
            IntPtr self,
            [Out] uint* Code);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DispatchCallbacksDelegate(
            IntPtr self,
            [In] uint Timeout);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ExitDispatchDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.Interface)] IDebugClient Client);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CreateClientDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr Client);             // out IDebugClient

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInputCallbacksDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.Interface)] IntPtr Callbacks);          // out IDebugInputCallbacks

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetInputCallbacksDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.Interface)] IDebugInputCallbacks Callbacks);

        /* GetOutputCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOutputCallbacksDelegate(
            IntPtr self,
            [Out] IntPtr Callbacks);    // out IDebugOutputCallbacks

        /* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetOutputCallbacksDelegate(
            IntPtr self,
            [In] IDebugOutputCallbacks Callbacks);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOutputMaskDelegate(
            IntPtr self,
            [Out] DEBUG_OUTPUT* Mask);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetOutputMaskDelegate(
            IntPtr self,
            [In] DEBUG_OUTPUT Mask);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOtherOutputMaskDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.Interface)] IDebugClient Client,
            [Out] DEBUG_OUTPUT* Mask);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetOtherOutputMaskDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.Interface)] IDebugClient Client,
            [In] DEBUG_OUTPUT Mask);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOutputWidthDelegate(
            IntPtr self,
            [Out] uint* Columns);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetOutputWidthDelegate(
            IntPtr self,
            [In] uint Columns);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetOutputLinePrefixDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* PrefixSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetOutputLinePrefixDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Prefix);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetIdentityDelegate(
            IntPtr self,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            [In] int BufferSize,
            [Out] uint* IdentitySize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputIdentityDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] uint Flags,
            [In][MarshalAs(UnmanagedType.LPStr)] string Format);

        /* GetEventCallbacks could a conversion thunk from the debugger engine so we can't specify a specific interface */

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEventCallbacksDelegate(
            IntPtr self,
            [Out] IntPtr Callbacks);    // out IDebugEventCallbacks

        /* We may have to pass a debugger engine conversion thunk back in so we can't specify a specific interface */

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetEventCallbacksDelegate(
            IntPtr self,
            [In] IDebugEventCallbacks Callbacks);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FlushCallbacksDelegate(
            IntPtr self);

        #endregion
    }
}