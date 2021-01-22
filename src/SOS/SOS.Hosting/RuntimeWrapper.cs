﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting
{
    internal sealed unsafe class RuntimeWrapper : COMCallableIUnknown, IDisposable
    {
        /// <summary>
        /// The runtime OS and type. Must match IRuntime::RuntimeConfiguration in runtime.h.
        /// </summary>
        enum RuntimeConfiguration
        {
            WindowsDesktop      = 0,
            WindowsCore         = 1,
            UnixCore            = 2,
            OSXCore             = 3,
            Unknown             = 4
        }

        private static readonly Guid IID_IRuntime = new Guid("A5F152B9-BA78-4512-9228-5091A4CB7E35");
        private static Guid IID_IXCLRDataProcess = new Guid("5c552ab6-fc09-4cb3-8e36-22fa03c798b7");
        private static Guid IID_ICorDebugProcess = new Guid("3d6f5f64-7538-11d3-8d5b-00104b35e7ef");

        #region DAC and DBI function delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int DllMainDelegate(
            IntPtr instance,
            int reason,
            IntPtr reserved);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CLRDataCreateInstanceDelegate(
            in Guid riid,
            IntPtr dacDataInterface,
            out IntPtr ppObj);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OpenVirtualProcessImpl2Delegate(
            ulong clrInstanceId,
            IntPtr dataTarget,
            [MarshalAs(UnmanagedType.LPWStr)] string dacModulePath,
            ref ClrDebuggingVersion maxDebuggerSupportedVersion,
            ref Guid riid,
            out IntPtr instance,
            out ClrDebuggingProcessFlags flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OpenVirtualProcessImplDelegate(
            ulong clrInstanceId,
            IntPtr dataTarget,
            IntPtr hDac,
            ref ClrDebuggingVersion maxDebuggerSupportedVersion,
            ref Guid riid,
            out IntPtr instance,
            out ClrDebuggingProcessFlags flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OpenVirtualProcessDelegate(
            ulong clrInstanceId,
            IntPtr dataTarget,
            IntPtr hDac,
            ref Guid riid,
            out IntPtr instance,
            out ClrDebuggingProcessFlags flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr LoadLibraryWDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string modulePath);

        #endregion

        private readonly ITarget _target;
        private readonly IRuntime _runtime;
        private readonly IDisposable _onFlushEvent;
        private IntPtr _clrDataProcess = IntPtr.Zero;
        private IntPtr _corDebugProcess = IntPtr.Zero;
        private IntPtr _dacHandle = IntPtr.Zero;
        private IntPtr _dbiHandle = IntPtr.Zero;

        public IntPtr IRuntime { get; }

        internal RuntimeWrapper(ITarget target, IRuntime runtime)
        {
            Debug.Assert(target != null);
            Debug.Assert(runtime != null);
            _target = target;
            _runtime = runtime;

            _onFlushEvent = target.OnFlushEvent.Register(() => {
                // TODO: there is a better way to flush _corDebugProcess with ICorDebugProcess4::ProcessStateChanged(FLUSH_ALL)
                _corDebugProcess = IntPtr.Zero;
                // TODO: there is a better way to flush _clrDataProcess with ICLRDataProcess::Flush()
                _clrDataProcess = IntPtr.Zero;
            });

            VTableBuilder builder = AddInterface(IID_IRuntime, validate: false);

            builder.AddMethod(new GetRuntimeConfigurationDelegate(GetRuntimeConfiguration));
            builder.AddMethod(new GetModuleAddressDelegate(GetModuleAddress));
            builder.AddMethod(new GetModuleSizeDelegate(GetModuleSize));
            builder.AddMethod(new GetRuntimeDirectoryDelegate(GetRuntimeDirectory));
            builder.AddMethod(new GetClrDataProcessDelegate(GetClrDataProcess));
            builder.AddMethod(new GetCorDebugInterfaceDelegate(GetCorDebugInterface));
            builder.AddMethod(new GetEEVersionDelegate(GetEEVersion));

            IRuntime = builder.Complete();

            AddRef();
        }

        public void Dispose()
        {
            _onFlushEvent.Dispose();
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~RuntimeWrapper()
        {
            Dispose(false);
        }

        private void Dispose(bool _)
        {
            if (_dacHandle != IntPtr.Zero)
            {
                DataTarget.PlatformFunctions.FreeLibrary(_dacHandle);
                _dacHandle = IntPtr.Zero;
            }
            if (_dbiHandle != IntPtr.Zero)
            {
                DataTarget.PlatformFunctions.FreeLibrary(_dbiHandle);
                _dbiHandle = IntPtr.Zero;
            }
        }

        #region IRuntime (native)

        private RuntimeConfiguration GetRuntimeConfiguration(
            IntPtr self)
        {
            switch (_runtime.RuntimeType)
            {
                case RuntimeType.Desktop:
                    return RuntimeConfiguration.WindowsDesktop;

                case RuntimeType.NetCore:
                case RuntimeType.SingleFile:
                    if (_target.OperatingSystem == OSPlatform.Windows)
                    {
                        return RuntimeConfiguration.WindowsCore;
                    }
                    else if (_target.OperatingSystem == OSPlatform.Linux || _target.OperatingSystem == OSPlatform.OSX)
                    {
                        return RuntimeConfiguration.UnixCore;
                    }
                    break;
            }
            return RuntimeConfiguration.Unknown;
        }

        private ulong GetModuleAddress(
            IntPtr self)
        {
            return _runtime.RuntimeModule.ImageBase;
        }

        private ulong GetModuleSize(
            IntPtr self)
        {
            return _runtime.RuntimeModule.ImageSize;
        }

        private string GetRuntimeDirectory(
            IntPtr self)
        {
            return Path.GetDirectoryName(_runtime.RuntimeModule.FileName);
        }

        private int GetClrDataProcess(
            IntPtr self,
            IntPtr* ppClrDataProcess)
        {
            if (ppClrDataProcess == null) {
                return HResult.E_INVALIDARG;
            }
            if (_clrDataProcess == IntPtr.Zero) {
                _clrDataProcess = CreateClrDataProcess();
            }
            *ppClrDataProcess = _clrDataProcess;
            if (*ppClrDataProcess == IntPtr.Zero) {
                return HResult.E_NOINTERFACE;
            }
            return HResult.S_OK;
        }

        private int GetCorDebugInterface(
            IntPtr self,
            IntPtr* ppCorDebugProcess)
        {
            if (ppCorDebugProcess == null) {
                return HResult.E_INVALIDARG;
            }
            if (_corDebugProcess == IntPtr.Zero) {
                _corDebugProcess = CreateCorDebugProcess();
            }
            *ppCorDebugProcess = _corDebugProcess;
            if (*ppCorDebugProcess == IntPtr.Zero) {
                return HResult.E_NOINTERFACE;
            }
            return HResult.S_OK;
        }

        private int GetEEVersion(
            IntPtr self,
            VS_FIXEDFILEINFO* pFileInfo,
            byte* fileVersionBuffer,
            int fileVersionBufferSizeInBytes)
        {
            IModuleService moduleService = _target.Services.GetService<IModuleService>();
            IModule module;
            try
            {
                module = moduleService.GetModuleFromBaseAddress(_runtime.RuntimeModule.ImageBase);
            }
            catch (DiagnosticsException)
            {
                return HResult.E_FAIL;
            }
            if (!module.Version.HasValue)
            {
                return HResult.E_FAIL;
            }
            pFileInfo->dwSignature = 0;
            pFileInfo->dwStrucVersion = 0;
            pFileInfo->dwFileFlagsMask = 0;
            pFileInfo->dwFileFlags = 0;

            VersionInfo versionInfo = module.Version.Value;
            pFileInfo->dwFileVersionMS = (uint)versionInfo.Minor | (uint)versionInfo.Major << 16;
            pFileInfo->dwFileVersionLS = (uint)versionInfo.Patch | (uint)versionInfo.Revision << 16;

            // Attempt to get the FileVersion string that contains version and the "built by" and commit id info
            if (fileVersionBuffer != null)
            {
                if (fileVersionBufferSizeInBytes > 0) {
                    *fileVersionBuffer = 0;
                }
                string versionString = module.VersionString;
                if (versionString != null)
                {
                    try
                    {
                        byte[] source = Encoding.ASCII.GetBytes(versionString + '\0');
                        Marshal.Copy(source, 0, new IntPtr(fileVersionBuffer), Math.Min(source.Length, (int)fileVersionBufferSizeInBytes));
                        return HResult.S_OK;
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return HResult.E_INVALIDARG;
                    }
                }
            }

            return HResult.S_OK;
        }

        #endregion

        private IntPtr CreateClrDataProcess()
        {
            IntPtr dacHandle = GetDacHandle();
            if (dacHandle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            var createInstance = SOSHost.GetDelegateFunction<CLRDataCreateInstanceDelegate>(dacHandle, "CLRDataCreateInstance");
            if (createInstance == null)
            {
                Trace.TraceError("Failed to obtain DAC CLRDataCreateInstance");
                return IntPtr.Zero;
            }
            var dataTarget = new DataTargetWrapper(_target, _runtime);
            int hr = createInstance(IID_IXCLRDataProcess, dataTarget.IDataTarget, out IntPtr unk);
            if (hr != 0)
            {
                Trace.TraceError($"CLRDataCreateInstance FAILED {hr:X8}");
                return IntPtr.Zero;
            }
            return unk;
        }

        private IntPtr CreateCorDebugProcess()
        {
            string dbiFilePath = _runtime.GetDbiFilePath();
            string dacFilePath = _runtime.GetDacFilePath();
            if (dbiFilePath == null || dacFilePath == null)
            {
                Trace.TraceError($"Could not find matching DBI {dbiFilePath ?? ""} or DAC {dacFilePath ?? ""} for this runtime: {_runtime.RuntimeModule.FileName}");
                return IntPtr.Zero;
            }
            if (_dbiHandle == IntPtr.Zero)
            {
                _dbiHandle = DataTarget.PlatformFunctions.LoadLibrary(dbiFilePath);
                if (_dbiHandle == IntPtr.Zero)
                {
                    Trace.TraceError($"DBI LoadLibrary({dbiFilePath}) FAILED");
                    return IntPtr.Zero;
                }
            }
            ClrDebuggingVersion maxDebuggerSupportedVersion = new ClrDebuggingVersion {
                StructVersion = 0,
                Major = 4,
                Minor = 0,
                Build = 0,
                Revision = 0,
            };
            var dataTarget = new CorDebugDataTargetWrapper(_target,  _runtime);
            ulong clrInstanceId = _runtime.RuntimeModule.ImageBase;
            int hresult = 0;

            var openVirtualProcessImpl2 = SOSHost.GetDelegateFunction<OpenVirtualProcessImpl2Delegate>(_dbiHandle, "OpenVirtualProcessImpl2");
            if (openVirtualProcessImpl2 != null)
            {
                hresult = openVirtualProcessImpl2(
                    clrInstanceId,
                    dataTarget.ICorDebugDataTarget,
                    dacFilePath,
                    ref maxDebuggerSupportedVersion,
                    ref IID_ICorDebugProcess,
                    out IntPtr corDebugProcess,
                    out ClrDebuggingProcessFlags flags);

                if (hresult != 0)
                {
                    Trace.TraceError($"DBI OpenVirtualProcessImpl2 FAILED 0x{hresult:X8}");
                    return IntPtr.Zero;
                }
                Trace.TraceInformation($"DBI OpenVirtualProcessImpl2 SUCCEEDED");
                return corDebugProcess;
            }

            IntPtr dacHandle = GetDacHandle();
            if (dacHandle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            // On Linux/MacOS the DAC module handle needs to be re-created using the DAC PAL instance
            // before being passed to DBI's OpenVirtualProcess* implementation. The DBI and DAC share 
            // the same PAL where dbgshim has it's own.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var loadLibraryFunction = SOSHost.GetDelegateFunction<LoadLibraryWDelegate>(dacHandle, "LoadLibraryW");
                if (loadLibraryFunction == null)
                {
                    Trace.TraceError($"Can not find the DAC LoadLibraryW export");
                    return IntPtr.Zero;
                }
                dacHandle = loadLibraryFunction(dacFilePath);
                if (dacHandle == IntPtr.Zero)
                {
                    Trace.TraceError($"DAC LoadLibraryW({dacFilePath}) FAILED");
                    return IntPtr.Zero;
                }
            }

            var openVirtualProcessImpl = SOSHost.GetDelegateFunction<OpenVirtualProcessImplDelegate>(_dbiHandle, "OpenVirtualProcessImpl");
            if (openVirtualProcessImpl != null)
            { 
                hresult = openVirtualProcessImpl(
                    clrInstanceId,
                    dataTarget.ICorDebugDataTarget,
                    dacHandle,
                    ref maxDebuggerSupportedVersion,
                    ref IID_ICorDebugProcess,
                    out IntPtr corDebugProcess,
                    out ClrDebuggingProcessFlags flags);

                if (hresult != 0)
                {
                    Trace.TraceError($"DBI OpenVirtualProcessImpl FAILED 0x{hresult:X8}");
                    return IntPtr.Zero;
                }
                Trace.TraceInformation($"DBI OpenVirtualProcessImpl SUCCEEDED");
                return corDebugProcess;
            }

            var openVirtualProcess = SOSHost.GetDelegateFunction<OpenVirtualProcessDelegate>(_dbiHandle, "OpenVirtualProcess");
            if (openVirtualProcess != null)
            { 
                hresult = openVirtualProcess(
                    clrInstanceId,
                    dataTarget.ICorDebugDataTarget,
                    dacHandle,
                    ref IID_ICorDebugProcess,
                    out IntPtr corDebugProcess,
                    out ClrDebuggingProcessFlags flags);

                if (hresult != 0)
                {
                    Trace.TraceError($"DBI OpenVirtualProcess FAILED 0x{hresult:X8}");
                    return IntPtr.Zero;
                }
                Trace.TraceInformation($"DBI OpenVirtualProcess SUCCEEDED");
                return corDebugProcess;
            }

            Trace.TraceError("DBI OpenVirtualProcess not found");
            return IntPtr.Zero;
        }

        private IntPtr GetDacHandle()
        {
            if (_dacHandle == IntPtr.Zero)
            {
                string dacFilePath = _runtime.GetDacFilePath();
                if (dacFilePath == null)
                {
                    Trace.TraceError($"Could not find matching DAC {dacFilePath ?? ""} for this runtime: {_runtime.RuntimeModule.FileName}");
                    return IntPtr.Zero;
                }
                _dacHandle = DataTarget.PlatformFunctions.LoadLibrary(dacFilePath);
                if (_dacHandle == IntPtr.Zero)
                {
                    Trace.TraceError($"DAC LoadLibrary({dacFilePath}) FAILED");
                    return IntPtr.Zero;
                }
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var dllmain = SOSHost.GetDelegateFunction<DllMainDelegate>(_dacHandle, "DllMain");
                    dllmain?.Invoke(_dacHandle, 1, IntPtr.Zero);
                }
            }
            return _dacHandle;
        }

        #region IRuntime delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate RuntimeConfiguration GetRuntimeConfigurationDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate ulong GetModuleAddressDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate ulong GetModuleSizeDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        private delegate string GetRuntimeDirectoryDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetClrDataProcessDelegate(
            [In] IntPtr self,
            [Out] IntPtr *ppClrDataProcess);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCorDebugInterfaceDelegate(
            [In] IntPtr self,
            [Out] IntPtr *ppCorDebugProcess);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEEVersionDelegate(
            [In] IntPtr self,
            [Out] VS_FIXEDFILEINFO* pFileInfo,
            [Out] byte* fileVersionBuffer,
            [In] int fileVersionBufferSizeInBytes);

        #endregion
    }
}
