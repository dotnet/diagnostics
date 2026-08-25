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
using Microsoft.FileFormats.ELF;
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

        public static Guid IID_IXCLRDataProcess = new("5c552ab6-fc09-4cb3-8e36-22fa03c798b7");
        public static Guid IID_ICorDebugProcess = new("3d6f5f64-7538-11d3-8d5b-00104b35e7ef");
        private static readonly Guid IID_IRuntime = new("A5F152B9-BA78-4512-9228-5091A4CB7E35");

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
            IntPtr dacHandle,
            ref ClrDebuggingVersion maxDebuggerSupportedVersion,
            ref Guid riid,
            out IntPtr instance,
            out ClrDebuggingProcessFlags flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OpenVirtualProcessDelegate(
            ulong clrInstanceId,
            IntPtr dataTarget,
            IntPtr dacHandle,
            ref Guid riid,
            out IntPtr instance,
            out ClrDebuggingProcessFlags flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr LoadLibraryWDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string modulePath);

        #endregion

        private readonly IServiceProvider _services;
        private readonly IRuntime _runtime;
        private IntPtr _clrDataProcess = IntPtr.Zero;
        private IntPtr _corDebugProcess = IntPtr.Zero;
        private IntPtr _dacHandle = IntPtr.Zero;
        private IntPtr _dbiHandle = IntPtr.Zero;
        private RuntimeLibraryProvider _libraryProvider;

        public IntPtr IRuntime { get; }

        public RuntimeWrapper(IServiceProvider services, IRuntime runtime)
        {
            Debug.Assert(services != null);
            Debug.Assert(runtime != null);
            _services = services;
            _runtime = runtime;

            VTableBuilder builder = AddInterface(IID_IRuntime, validate: false);

            builder.AddMethod(new GetRuntimeConfigurationDelegate(GetRuntimeConfiguration));
            builder.AddMethod(new GetModuleAddressDelegate(GetModuleAddress));
            builder.AddMethod(new GetModuleSizeDelegate(GetModuleSize));
            builder.AddMethod(new SetRuntimeDirectoryDelegate(SetRuntimeDirectory));
            builder.AddMethod(new GetRuntimeDirectoryDelegate(GetRuntimeDirectory));
            builder.AddMethod(new GetClrDataProcessDelegate(GetClrDataProcess));
            builder.AddMethod(new GetCorDebugInterfaceDelegate(GetCorDebugInterface));
            builder.AddMethod(new GetEEVersionDelegate(GetEEVersion));
            builder.AddMethod(new GetCDacLoadPolicyDelegate(GetCDacLoadPolicy));

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
            if (_corDebugProcess != IntPtr.Zero)
            {
                ComWrapper.ReleaseWithCheck(_corDebugProcess);
                _corDebugProcess = IntPtr.Zero;
            }
            if (_clrDataProcess != IntPtr.Zero)
            {
                ComWrapper.ReleaseWithCheck(_clrDataProcess);
                _clrDataProcess = IntPtr.Zero;
            }
            if (_dacHandle != IntPtr.Zero)
            {
                // Previously, the DAC was freed here, but as we transition to the cDAC which uses NativeAOT,
                // it is no longer possible to free the DAC library when it is using the shimmed cDAC.
                _dacHandle = IntPtr.Zero;
            }
            if (_dbiHandle != IntPtr.Zero)
            {
                DataTarget.PlatformFunctions.FreeLibrary(_dbiHandle);
                _dbiHandle = IntPtr.Zero;
            }
            (_libraryProvider as IDisposable)?.Dispose();
            _libraryProvider = null;
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
            CDacLoadPolicy policy,
            IntPtr* ppClrDataProcess)
        {
            if (ppClrDataProcess == null)
            {
                return HResult.E_INVALIDARG;
            }
            *ppClrDataProcess = IntPtr.Zero;
            bool cdacOnly = policy == CDacLoadPolicy.OnlyUseCDac;
            bool useCDac = CDacPolicy.ShouldTryCDac(policy);

            int cdacActivationResult = HResult.E_NOINTERFACE;
            if (useCDac)
            {
                try
                {
                    Trace.TraceInformation($"Runtime #{_runtime.Id} native data-access: requesting an IXCLRDataProcess (cDAC preferred)");
                    cdacActivationResult = _runtime.GetClrDataProcessFromCDac(out IntPtr cdacDataProcess);
                    *ppClrDataProcess = cdacDataProcess;
                    if (cdacActivationResult >= 0 && cdacDataProcess != IntPtr.Zero)
                    {
                        Trace.TraceInformation($"Runtime #{_runtime.Id} native data-access: received an IXCLRDataProcess");
                    }
                    else
                    {
                        Trace.TraceInformation(cdacOnly
                            ? $"Runtime #{_runtime.Id} native data-access: no IXCLRDataProcess was created under forced cDAC policy"
                            : $"Runtime #{_runtime.Id} native data-access: no IXCLRDataProcess was created; falling back to the in-box DAC");
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                    cdacActivationResult = ex.HResult;
                }
            }
            if (*ppClrDataProcess == IntPtr.Zero && cdacOnly)
            {
                Trace.TraceError($"Runtime #{_runtime.Id} native data-access: cDAC was forced but could not service this runtime; not falling back to the DAC");
                return cdacActivationResult;
            }
            if (*ppClrDataProcess == IntPtr.Zero)
            {
                if (_clrDataProcess == IntPtr.Zero)
                {
                    try
                    {
                        Trace.TraceInformation($"Runtime #{_runtime.Id} native data-access: creating IXCLRDataProcess from the in-box DAC");
                        _clrDataProcess = CreateClrDataProcessFromDac(GetDacHandle());
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError(ex.ToString());
                    }
                }
                *ppClrDataProcess = _clrDataProcess;
            }
            if (*ppClrDataProcess == IntPtr.Zero)
            {
                return HResult.E_NOINTERFACE;
            }
            return HResult.S_OK;
        }

        private CDacLoadPolicy GetCDacLoadPolicy(IntPtr self)
        {
            return _services.GetService<ISettingsService>()?.CDacLoadPolicy ?? CDacLoadPolicy.PreferCDac;
        }

        private int GetCorDebugInterface(
            IntPtr self,
            IntPtr* ppCorDebugProcess)
        {
            if (ppCorDebugProcess == null)
            {
                return HResult.E_INVALIDARG;
            }
            int result = HResult.S_OK;
            if (_corDebugProcess == IntPtr.Zero)
            {
                try
                {
                    result = CreateCorDebugProcess(out _corDebugProcess);
                }
                catch (Exception ex)
                {
                    Trace.TraceError(ex.ToString());
                    result = ex.HResult;
                }
            }
            *ppCorDebugProcess = _corDebugProcess;
            if (*ppCorDebugProcess == IntPtr.Zero)
            {
                return result < 0 ? result : HResult.E_NOINTERFACE;
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

        private IntPtr CreateClrDataProcessFromDac(IntPtr dacHandle)
        {
            if (dacHandle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }
            CLRDataCreateInstanceDelegate createInstance = SOSHost.GetDelegateFunction<CLRDataCreateInstanceDelegate>(dacHandle, "CLRDataCreateInstance");
            if (createInstance == null)
            {
                Trace.TraceError("Failed to obtain DAC CLRDataCreateInstance");
                return IntPtr.Zero;
            }
            DataTargetWrapper dataTarget = new(_services, _runtime);
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

        private int CreateCorDebugProcess(out IntPtr corDebugProcess)
        {
            if (_runtime.RuntimeType == RuntimeType.Desktop)
            {
                corDebugProcess = CreateDesktopCorDebugProcess();
                return corDebugProcess != IntPtr.Zero ? HResult.S_OK : HResult.E_NOINTERFACE;
            }

            corDebugProcess = IntPtr.Zero;
            _libraryProvider ??= new RuntimeLibraryProvider(
                _runtime.GetDbiFilePath,
                GetVerifiedDacFilePath);

            IClrDataProcessActivator activator = _services.GetService<IClrDataProcessActivator>();
            if (activator is null)
            {
                return HResult.E_NOINTERFACE;
            }

            CDacLoadPolicy policy = _runtime.RuntimeType == RuntimeType.Desktop
                ? CDacLoadPolicy.UseLegacyDac
                : _services.GetService<ISettingsService>()?.CDacLoadPolicy ?? CDacLoadPolicy.PreferCDac;
            return activator.CreateCorDebugProcess(
                _runtime,
                _libraryProvider.ILibraryProvider,
                policy,
                out corDebugProcess);
        }

        private string GetVerifiedDacFilePath()
        {
            string dacFilePath = _runtime.GetDacFilePath(out _);
            return dacFilePath is not null && GetDacHandle() != IntPtr.Zero
                ? dacFilePath
                : null;
        }

        private IntPtr CreateDesktopCorDebugProcess()
        {
            string dbiFilePath = _runtime.GetDbiFilePath();
            IntPtr dacHandle = GetDacHandle();
            string dacFilePath = _runtime.GetDacFilePath(out _);
            if (dbiFilePath is null || dacHandle == IntPtr.Zero || dacFilePath is null)
            {
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
            }

            ClrDebuggingVersion maxDebuggerSupportedVersion = new()
            {
                StructVersion = 0,
                Major = 4,
                Minor = 0,
                Build = 0,
                Revision = 0,
            };
            CorDebugDataTargetWrapper dataTarget = new(_services, _runtime);
            try
            {
                OpenVirtualProcessImpl2Delegate openVirtualProcessImpl2 =
                    SOSHost.GetDelegateFunction<OpenVirtualProcessImpl2Delegate>(_dbiHandle, "OpenVirtualProcessImpl2");
                if (openVirtualProcessImpl2 is not null)
                {
                    int hr = openVirtualProcessImpl2(
                        _runtime.RuntimeModule.ImageBase,
                        dataTarget.ICorDebugDataTarget,
                        dacFilePath,
                        ref maxDebuggerSupportedVersion,
                        ref IID_ICorDebugProcess,
                        out IntPtr corDebugProcess,
                        out _);
                    return hr == HResult.S_OK ? corDebugProcess : IntPtr.Zero;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    LoadLibraryWDelegate loadLibrary =
                        SOSHost.GetDelegateFunction<LoadLibraryWDelegate>(dacHandle, "LoadLibraryW");
                    dacHandle = loadLibrary?.Invoke(dacFilePath) ?? IntPtr.Zero;
                    if (dacHandle == IntPtr.Zero)
                    {
                        return IntPtr.Zero;
                    }
                }

                OpenVirtualProcessImplDelegate openVirtualProcessImpl =
                    SOSHost.GetDelegateFunction<OpenVirtualProcessImplDelegate>(_dbiHandle, "OpenVirtualProcessImpl");
                if (openVirtualProcessImpl is not null)
                {
                    int hr = openVirtualProcessImpl(
                        _runtime.RuntimeModule.ImageBase,
                        dataTarget.ICorDebugDataTarget,
                        dacHandle,
                        ref maxDebuggerSupportedVersion,
                        ref IID_ICorDebugProcess,
                        out IntPtr corDebugProcess,
                        out _);
                    return hr == HResult.S_OK ? corDebugProcess : IntPtr.Zero;
                }

                OpenVirtualProcessDelegate openVirtualProcess =
                    SOSHost.GetDelegateFunction<OpenVirtualProcessDelegate>(_dbiHandle, "OpenVirtualProcess");
                if (openVirtualProcess is not null)
                {
                    int hr = openVirtualProcess(
                        _runtime.RuntimeModule.ImageBase,
                        dataTarget.ICorDebugDataTarget,
                        dacHandle,
                        ref IID_ICorDebugProcess,
                        out IntPtr corDebugProcess,
                        out _);
                    return hr == HResult.S_OK ? corDebugProcess : IntPtr.Zero;
                }
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
                string dacFilePath = _runtime.GetDacFilePath(out bool verifySignature);
                if (dacFilePath == null)
                {
                    Trace.TraceError($"Could not find matching DAC for this runtime: {_runtime.RuntimeModule.FileName}");
                    return IntPtr.Zero;
                }
                _dacHandle = LoadDacLibrary(dacFilePath, verifySignature);
            }
            return _dacHandle;
        }

        private static IntPtr LoadDacLibrary(string dacFilePath, bool verifySignature)
        {
            IntPtr dacHandle = IntPtr.Zero;
            IDisposable fileLock = null;
            try
            {
                if (verifySignature)
                {
                    Trace.TraceInformation($"Verifying DAC signing and cert {dacFilePath}");

                    // Check if the DAC cert is valid before loading
                    if (!AuthenticodeUtil.VerifyDacDll(dacFilePath, out fileLock))
                    {
                        return IntPtr.Zero;
                    }
                }
                try
                {
                    dacHandle = DataTarget.PlatformFunctions.LoadLibrary(dacFilePath);
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
                {
                    Trace.TraceError($"LoadLibrary({dacFilePath}) FAILED {ex}");
                    return IntPtr.Zero;
                }
            }
            finally
            {
                // Keep DAC file locked until it loaded
                fileLock?.Dispose();
            }
            Debug.Assert(dacHandle != IntPtr.Zero);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                DllMainDelegate dllmain = SOSHost.GetDelegateFunction<DllMainDelegate>(dacHandle, "DllMain");
                dllmain?.Invoke(dacHandle, 1, IntPtr.Zero);
            }
            return dacHandle;
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
            [In] CDacLoadPolicy policy,
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

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate CDacLoadPolicy GetCDacLoadPolicyDelegate(
            [In] IntPtr self);

        #endregion
    }
}
