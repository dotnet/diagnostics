// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Hosting
{
    [ServiceExport(Scope = ServiceScope.Runtime)]
    public sealed unsafe class RuntimeWrapper : COMCallableIUnknown, IDisposable
    {
        /// <summary>
        /// The runtime OS and type. Must match IRuntime::RuntimeConfiguration in runtime.h.
        /// </summary>
        private enum RuntimeConfiguration
        {
            WindowsDesktop = 0,
            WindowsCore = 1,
            UnixCore = 2,
            OSXCore = 3,
            Unknown = 4
        }

        public static Guid IID_IXCLRDataProcess = new Guid("5c552ab6-fc09-4cb3-8e36-22fa03c798b7");
        public static Guid IID_ICorDebugProcess = new Guid("3d6f5f64-7538-11d3-8d5b-00104b35e7ef");
        private static readonly Guid IID_IRuntime = new Guid("A5F152B9-BA78-4512-9228-5091A4CB7E35");

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

        private readonly IServiceProvider _services;
        private readonly IRuntime _runtime;
        private readonly IDisposable _onFlushEvent;
        private IntPtr _clrDataProcess = IntPtr.Zero;
        private IntPtr _corDebugProcess = IntPtr.Zero;
        private IntPtr _dacHandle = IntPtr.Zero;
        private IntPtr _dbiHandle = IntPtr.Zero;

        public IntPtr IRuntime { get; }

        public RuntimeWrapper(IServiceProvider services, IRuntime runtime)
        {
            Debug.Assert(services != null);
            Debug.Assert(runtime != null);
            _services = services;
            _runtime = runtime;
            _onFlushEvent = runtime.Target.OnFlushEvent.Register(Flush);

            VTableBuilder builder = AddInterface(IID_IRuntime, validate: false);

            builder.AddMethod(new GetRuntimeConfigurationDelegate(GetRuntimeConfiguration));
            builder.AddMethod(new GetModuleAddressDelegate(GetModuleAddress));
            builder.AddMethod(new GetModuleSizeDelegate(GetModuleSize));
            builder.AddMethod(new SetRuntimeDirectoryDelegate(SetRuntimeDirectory));
            builder.AddMethod(new GetRuntimeDirectoryDelegate(GetRuntimeDirectory));
            builder.AddMethod(new GetClrDataProcessDelegate(GetClrDataProcess));
            builder.AddMethod(new GetCorDebugInterfaceDelegate(GetCorDebugInterface));
            builder.AddMethod(new GetEEVersionDelegate(GetEEVersion));

            IRuntime = builder.Complete();

            AddRef();
        }

        void IDisposable.Dispose()
        {
            Trace.TraceInformation("RuntimeWrapper.Dispose");
            this.ReleaseWithCheck();
        }

        protected override void Destroy()
        {
            Trace.TraceInformation("RuntimeWrapper.Destroy");
            _onFlushEvent.Dispose();
            Flush();
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

        private void Flush()
        {
            // TODO: there is a better way to flush _corDebugProcess with ICorDebugProcess4::ProcessStateChanged(FLUSH_ALL)
            if (_corDebugProcess != IntPtr.Zero)
            {
                ComWrapper.ReleaseWithCheck(_corDebugProcess);
                _corDebugProcess = IntPtr.Zero;
            }
            // TODO: there is a better way to flush _clrDataProcess with ICLRDataProcess::Flush()
            if (_clrDataProcess != IntPtr.Zero)
            {
                ComWrapper.ReleaseWithCheck(_clrDataProcess);
                _clrDataProcess = IntPtr.Zero;
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
                    if (_runtime.Target.OperatingSystem == OSPlatform.Windows)
                    {
                        return RuntimeConfiguration.WindowsCore;
                    }
                    else if (_runtime.Target.OperatingSystem == OSPlatform.Linux || _runtime.Target.OperatingSystem == OSPlatform.OSX)
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

        private void SetRuntimeDirectory(
            IntPtr self,
            string runtimeModuleDirectory)
        {
            _runtime.RuntimeModuleDirectory = runtimeModuleDirectory;
        }

        private string GetRuntimeDirectory(
            IntPtr self)
        {
            if (_runtime.RuntimeModuleDirectory is not null)
            {
                return _runtime.RuntimeModuleDirectory;
            }
            return Path.GetDirectoryName(_runtime.RuntimeModule.FileName);
        }

        private int GetClrDataProcess(
            IntPtr self,
            IntPtr* ppClrDataProcess)
        {
            if (ppClrDataProcess == null)
            {
                return HResult.E_INVALIDARG;
            }
            if (_clrDataProcess == IntPtr.Zero)
            {
                try
                {
                    _clrDataProcess = CreateClrDataProcess();
                }
                catch (Exception)
                {
                    return HResult.E_NOINTERFACE;
                }
            }
            *ppClrDataProcess = _clrDataProcess;
            if (*ppClrDataProcess == IntPtr.Zero)
            {
                return HResult.E_NOINTERFACE;
            }
            return HResult.S_OK;
        }

        private int GetCorDebugInterface(
            IntPtr self,
            IntPtr* ppCorDebugProcess)
        {
            if (ppCorDebugProcess == null)
            {
                return HResult.E_INVALIDARG;
            }
            if (_corDebugProcess == IntPtr.Zero)
            {
                _corDebugProcess = CreateCorDebugProcess();
            }
            *ppCorDebugProcess = _corDebugProcess;
            if (*ppCorDebugProcess == IntPtr.Zero)
            {
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
            if (pFileInfo == null)
            {
                return HResult.E_INVALIDARG;
            }
            pFileInfo->dwSignature = 0;
            pFileInfo->dwStrucVersion = 0;
            pFileInfo->dwFileFlagsMask = 0;
            pFileInfo->dwFileFlags = 0;
            pFileInfo->dwFileVersionMS = 0;
            pFileInfo->dwFileVersionLS = 0;

            Version version = _runtime.RuntimeVersion;
            if (version is not null)
            {
                pFileInfo->dwFileVersionMS = (uint)version.Minor & 0xffff | (uint)version.Major << 16;
                pFileInfo->dwFileVersionLS = (uint)version.Revision & 0xffff | (uint)version.Build << 16;
            }

            // Attempt to get the FileVersion string that contains version and the "built by" and commit id info
            if (fileVersionBuffer != null)
            {
                if (fileVersionBufferSizeInBytes > 0)
                {
                    *fileVersionBuffer = 0;
                }
                string versionString = _runtime.RuntimeModule.GetVersionString();
                if (versionString != null)
                {
                    try
                    {
                        byte[] source = Encoding.ASCII.GetBytes(versionString + '\0');
                        Marshal.Copy(source, 0, new IntPtr(fileVersionBuffer), Math.Min(source.Length, fileVersionBufferSizeInBytes));
                    }
                    catch (ArgumentOutOfRangeException)
                    {
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
            var dataTarget = new DataTargetWrapper(_services, _runtime);
            try
            {
                int hr = createInstance(IID_IXCLRDataProcess, dataTarget.IDataTarget, out IntPtr unk);
                if (hr != 0)
                {
                    Trace.TraceError($"CLRDataCreateInstance FAILED {hr:X8}");
                    return IntPtr.Zero;
                }
                return unk;
            }
            finally
            {
                dataTarget.ReleaseWithCheck();
            }
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
                try
                {
                    _dbiHandle = DataTarget.PlatformFunctions.LoadLibrary(dbiFilePath);
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
                {
                    Trace.TraceError($"LoadLibrary({dbiFilePath}) FAILED {ex}");
                    return IntPtr.Zero;
                }
                Debug.Assert(_dbiHandle != IntPtr.Zero);
            }
            ClrDebuggingVersion maxDebuggerSupportedVersion = new ClrDebuggingVersion
            {
                StructVersion = 0,
                Major = 4,
                Minor = 0,
                Build = 0,
                Revision = 0,
            };
            var dataTarget = new CorDebugDataTargetWrapper(_services, _runtime);
            ulong clrInstanceId = _runtime.RuntimeModule.ImageBase;
            int hresult = 0;
            try
            {
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
            finally
            {
                dataTarget.ReleaseWithCheck();
            }
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
                try
                {
                    _dacHandle = DataTarget.PlatformFunctions.LoadLibrary(dacFilePath);
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
                {
                    Trace.TraceError($"LoadLibrary({dacFilePath}) FAILED {ex}");
                    return IntPtr.Zero;
                }
                Debug.Assert(_dacHandle != IntPtr.Zero);
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
        private delegate void SetRuntimeDirectoryDelegate(
            [In] IntPtr self,
            [In, MarshalAs(UnmanagedType.LPStr)] string runtimeModuleDirectory);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        private delegate string GetRuntimeDirectoryDelegate(
            [In] IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetClrDataProcessDelegate(
            [In] IntPtr self,
            [Out] IntPtr* ppClrDataProcess);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetCorDebugInterfaceDelegate(
            [In] IntPtr self,
            [Out] IntPtr* ppCorDebugProcess);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetEEVersionDelegate(
            [In] IntPtr self,
            [Out] VS_FIXEDFILEINFO* pFileInfo,
            [Out] byte* fileVersionBuffer,
            [In] int fileVersionBufferSizeInBytes);

        #endregion
    }
}
