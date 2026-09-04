// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Hosting
{
    /// <summary>
    /// Creates CLR data-access interfaces with ICLRDebugging.
    /// </summary>
    public sealed class ClrDataProcessActivator : IClrDataProcessActivator
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CLRCreateInstanceDelegate(in Guid clsid, in Guid riid, out IntPtr pInterface);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CLRDataCreateInstanceDelegate(in Guid riid, IntPtr dataTarget, out IntPtr pInterface);

        private readonly IHostAssetResolver _assetResolver;
        // Serializes initialization and use of the shared ICLRDebugging instance.
        private readonly object _lock = new();

        private bool _initialized;
        private CLRCreateInstanceDelegate _clrCreateInstance;
        private ICLRDebugging _dataAccessClrDebugging;
        private bool _cdacInitialized;
        private CLRDataCreateInstanceDelegate _cdacCreateInstance;

        private ClrDataProcessActivator(IHostAssetResolver assetResolver)
        {
            _assetResolver = assetResolver;
        }

        [ServiceExport(Scope = ServiceScope.Global)]
        public static IClrDataProcessActivator Create(IHostAssetResolver assetResolver)
        {
            return new ClrDataProcessActivator(assetResolver);
        }

        public int CreateClrDataProcessFromCDac(IRuntime runtime, out IClrDataProcess clrDataProcess)
        {
            clrDataProcess = null;
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (runtime.RuntimeType == RuntimeType.NativeAOT)
            {
                return CreateClrDataProcessFromCDacLibrary(runtime, out clrDataProcess);
            }

            ICLRDebugging clrDebugging = GetOrCreateDataAccessClrDebugging();
            if (clrDebugging is null)
            {
                return HResult.E_NOINTERFACE;
            }

            DataTargetWrapper dataTarget = new(runtime.Services, runtime);

            ClrDebuggingVersion maxDebuggerSupportedVersion = new()
            {
                StructVersion = 0,
                Major = 4,
                Minor = 0,
                Build = 0,
                Revision = 0,
            };

            // By passing null for the libraryProvider we are indicating that dbgshim should only evaluate the cDAC creation path.
            Guid riidProcess = RuntimeWrapper.IID_IXCLRDataProcess;
            HResult hr = clrDebugging.OpenVirtualProcess(
                runtime.RuntimeModule.ImageBase,
                dataTarget.IDataTarget,
                libraryProvider: IntPtr.Zero,
                maxDebuggerSupportedVersion,
                in riidProcess,
                out IntPtr clrDataProcessInterface,
                out _,
                out _);

            if (!hr || clrDataProcessInterface == IntPtr.Zero)
            {
                HResult result = hr ? HResult.E_NOINTERFACE : hr;
                Trace.TraceInformation($"ClrDataProcessActivator: dbgshim declined runtime #{runtime.Id} (hr={result:x8}).");
                if (clrDataProcessInterface != IntPtr.Zero)
                {
                    COMHelper.Release(clrDataProcessInterface);
                }
                dataTarget.ReleaseWithCheck();
                return result;
            }

            Trace.TraceInformation($"ClrDataProcessActivator: activated IXCLRDataProcess for runtime #{runtime.Id} via dbgshim.");
            clrDataProcess = new ClrDataProcess(clrDataProcessInterface, dataTarget);
            return hr;
        }

        private int CreateClrDataProcessFromCDacLibrary(
            IRuntime runtime,
            out IClrDataProcess clrDataProcess)
        {
            clrDataProcess = null;
            CLRDataCreateInstanceDelegate createInstance = GetOrCreateCDacCreateInstance();
            if (createInstance is null)
            {
                return HResult.E_NOINTERFACE;
            }

            DataTargetWrapper dataTarget = new(runtime.Services, runtime);
            Guid iid = RuntimeWrapper.IID_IXCLRDataProcess;
            HResult hr = createInstance(iid, dataTarget.IDataTarget, out IntPtr clrDataProcessInterface);
            if (!hr || clrDataProcessInterface == IntPtr.Zero)
            {
                HResult result = hr ? HResult.E_NOINTERFACE : hr;
                if (clrDataProcessInterface != IntPtr.Zero)
                {
                    COMHelper.Release(clrDataProcessInterface);
                }
                dataTarget.ReleaseWithCheck();
                Trace.TraceInformation($"ClrDataProcessActivator: cDAC declined NativeAOT runtime #{runtime.Id} (hr={result:x8}).");
                return result;
            }

            Trace.TraceInformation($"ClrDataProcessActivator: activated NativeAOT runtime #{runtime.Id} directly through the cDAC.");
            clrDataProcess = new ClrDataProcess(clrDataProcessInterface, dataTarget);
            return hr;
        }

        public int CreateCorDebugProcess(
            IRuntime runtime,
            IntPtr libraryProvider,
            CDacLoadPolicy policy,
            out IntPtr corDebugProcess)
        {
            corDebugProcess = IntPtr.Zero;
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            ICLRDebugging clrDebugging = CreateClrDebugging();
            if (clrDebugging is null)
            {
                return HResult.E_NOINTERFACE;
            }
            try
            {
                HResult policyResult = SetCDacLoadPolicy(clrDebugging, (DbgShimCDacLoadPolicy)policy);
                if (!policyResult)
                {
                    return policyResult;
                }

                ClrDebuggingVersion maxDebuggerSupportedVersion = new()
                {
                    StructVersion = 0,
                    Major = 4,
                    Minor = 0,
                    Build = 0,
                    Revision = 0,
                };

                CorDebugDataTargetWrapper dataTarget = new(runtime.Services, runtime);
                try
                {
                    Guid riidProcess = RuntimeWrapper.IID_ICorDebugProcess;
                    HResult hr = clrDebugging.OpenVirtualProcess(
                        runtime.RuntimeModule.ImageBase,
                        dataTarget.ICorDebugDataTarget,
                        libraryProvider,
                        maxDebuggerSupportedVersion,
                        in riidProcess,
                        out corDebugProcess,
                        out _,
                        out _);
                    if (!hr || corDebugProcess == IntPtr.Zero)
                    {
                        HResult result = hr ? HResult.E_NOINTERFACE : hr;
                        if (corDebugProcess != IntPtr.Zero)
                        {
                            COMHelper.Release(corDebugProcess);
                            corDebugProcess = IntPtr.Zero;
                        }
                        Trace.TraceInformation($"ClrDataProcessActivator: dbgshim declined DBI activation for runtime #{runtime.Id} (hr={result:x8}).");
                        return result;
                    }

                    Trace.TraceInformation($"ClrDataProcessActivator: activated ICorDebugProcess for runtime #{runtime.Id} via dbgshim.");
                    return hr;
                }
                finally
                {
                    dataTarget.ReleaseWithCheck();
                }
            }
            finally
            {
                COMHelper.Release(clrDebugging.InterfacePointer);
            }
        }

        private sealed class ClrDataProcess : IClrDataProcess
        {
            private IntPtr _interface;
            private DataTargetWrapper _dataTarget;

            public ClrDataProcess(IntPtr @interface, DataTargetWrapper dataTarget)
            {
                _interface = @interface;
                _dataTarget = dataTarget;
            }

            public IntPtr Interface => _interface;

            public void Dispose()
            {
                IntPtr @interface = Interlocked.Exchange(ref _interface, IntPtr.Zero);
                if (@interface != IntPtr.Zero)
                {
                    COMHelper.Release(@interface);
                }
                Interlocked.Exchange(ref _dataTarget, null)?.ReleaseWithCheck();
            }
        }

        private static HResult SetCDacLoadPolicy(ICLRDebugging clrDebugging, DbgShimCDacLoadPolicy policy)
        {
            if (!COMHelper.QueryInterface(clrDebugging.InterfacePointer, ICLRDebuggingPolicy.IID_ICLRDebuggingPolicy, out IntPtr policyPtr))
            {
                Trace.TraceError($"ClrDataProcessActivator: dbgshim does not support ICLRDebuggingPolicy (requested policy={policy}).");
                return HResult.E_NOINTERFACE;
            }

            try
            {
                HResult hr = ICLRDebuggingPolicy.Create(policyPtr).SetCDacLoadPolicy(policy);
                if (!hr)
                {
                    Trace.TraceError($"ClrDataProcessActivator: SetCDacLoadPolicy({policy}) failed (hr={hr:x8}).");
                    return hr;
                }
                return hr;
            }
            finally
            {
                COMHelper.Release(policyPtr);
            }
        }

        private ICLRDebugging GetOrCreateDataAccessClrDebugging()
        {
            lock (_lock)
            {
                if (_dataAccessClrDebugging is not null)
                {
                    return _dataAccessClrDebugging;
                }

                ICLRDebugging clrDebugging = CreateClrDebugging();
                if (clrDebugging is not null)
                {
                    HResult hr = SetCDacLoadPolicy(clrDebugging, DbgShimCDacLoadPolicy.CDacOnly);
                    if (!hr)
                    {
                        COMHelper.Release(clrDebugging.InterfacePointer);
                        return null;
                    }
                    _dataAccessClrDebugging = clrDebugging;
                }
                return _dataAccessClrDebugging;
            }
        }

        private ICLRDebugging CreateClrDebugging()
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    string dbgshimPath = GetDbgShimPath();
                    if (dbgshimPath is null || !File.Exists(dbgshimPath))
                    {
                        Trace.TraceInformation($"ClrDataProcessActivator: dbgshim not found at '{dbgshimPath}'.");
                        return null;
                    }

                    try
                    {
                        IntPtr dbgshimHandle = DataTarget.PlatformFunctions.LoadLibrary(dbgshimPath);
                        IntPtr createInstance = DataTarget.PlatformFunctions.GetLibraryExport(dbgshimHandle, "CLRCreateInstance");
                        if (createInstance == IntPtr.Zero)
                        {
                            Trace.TraceError("ClrDataProcessActivator: dbgshim!CLRCreateInstance export not found.");
                            return null;
                        }
                        _clrCreateInstance =
                            (CLRCreateInstanceDelegate)Marshal.GetDelegateForFunctionPointer(createInstance, typeof(CLRCreateInstanceDelegate));
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                    {
                        Trace.TraceError($"ClrDataProcessActivator: failed to load dbgshim: {ex.Message}");
                        return null;
                    }
                }

                if (_clrCreateInstance is null)
                {
                    return null;
                }

                HResult hr = _clrCreateInstance(
                    ICLRDebugging.CLSID_ICLRDebugging,
                    ICLRDebugging.IID_ICLRDebugging,
                    out IntPtr punk);
                if (!hr || punk == IntPtr.Zero)
                {
                    Trace.TraceError($"ClrDataProcessActivator: CLRCreateInstance failed (hr={hr:x8}).");
                    return null;
                }
                return ICLRDebugging.Create(punk);
            }
        }

        private CLRDataCreateInstanceDelegate GetOrCreateCDacCreateInstance()
        {
            lock (_lock)
            {
                if (_cdacInitialized)
                {
                    return _cdacCreateInstance;
                }
                _cdacInitialized = true;

                string cdacPath = _assetResolver?.GetCDacPath();
                if (string.IsNullOrEmpty(cdacPath) || !File.Exists(cdacPath))
                {
                    Trace.TraceInformation($"ClrDataProcessActivator: cDAC not found at '{cdacPath}'.");
                    return null;
                }

                try
                {
                    // The cDAC is a co-located tool asset and remains loaded for the host lifetime.
                    IntPtr cdacHandle = DataTarget.PlatformFunctions.LoadLibrary(cdacPath);
                    IntPtr createInstance = DataTarget.PlatformFunctions.GetLibraryExport(cdacHandle, "CLRDataCreateInstance");
                    if (createInstance == IntPtr.Zero)
                    {
                        Trace.TraceError("ClrDataProcessActivator: cDAC!CLRDataCreateInstance export not found.");
                        return null;
                    }
                    _cdacCreateInstance =
                        (CLRDataCreateInstanceDelegate)Marshal.GetDelegateForFunctionPointer(
                            createInstance,
                            typeof(CLRDataCreateInstanceDelegate));
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    Trace.TraceError($"ClrDataProcessActivator: failed to load cDAC: {ex.Message}");
                }
                return _cdacCreateInstance;
            }
        }

        private string GetDbgShimPath()
        {
            string directory = _assetResolver?.NativeBinariesDirectory;
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            string fileName;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fileName = "dbgshim.dll";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                fileName = "libdbgshim.dylib";
            }
            else
            {
                fileName = "libdbgshim.so";
            }

            return Path.Combine(directory, fileName);
        }
    }
}
