// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Hosting
{
    /// <summary>
    /// The SOS-hosting implementation of <see cref="IClrDataProcessActivator"/>. It loads the
    /// dbgshim that ships next to the native sos module, builds a runtime-bound
    /// <see cref="DataTargetWrapper"/> (the ICLRDataTarget plus the contract/runtime/metadata
    /// locators the cDAC needs), and asks dbgshim to open the runtime. dbgshim prefers the
    /// co-located cDAC; the resulting IXCLRDataProcess is handed back so ClrMD can build a runtime
    /// over it without loading a DAC itself.
    ///
    /// The library provider is intentionally not supplied here yet: under the prefer-cDAC policy a
    /// serviceable target is opened by the co-located cDAC without consulting a provider, and when
    /// the cDAC declines this returns <see cref="IntPtr.Zero"/> so the caller falls back to its
    /// existing DAC load path. A production library provider for in-dbgshim DAC fallback is Phase 4
    /// work.
    /// </summary>
    public sealed class ClrDataProcessActivator : IClrDataProcessActivator
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int CLRCreateInstanceDelegate(in Guid clsid, in Guid riid, out IntPtr pInterface);

        private readonly IHostAssetResolver _assetResolver;
        private readonly object _lock = new();

        // Keeps the data targets alive for the lifetime of this (global) service. The native cDAC
        // holds a reference to each ICLRDataTarget CCW, so the managed wrapper must not be collected
        // while a runtime backed by it is in use.
        private readonly List<DataTargetWrapper> _dataTargets = new();

        private bool _initialized;
        private ICLRDebugging _clrDebugging;

        private ClrDataProcessActivator(IHostAssetResolver assetResolver)
        {
            _assetResolver = assetResolver;
        }

        [ServiceExport(Scope = ServiceScope.Global)]
        public static IClrDataProcessActivator Create(IHostAssetResolver assetResolver)
        {
            return new ClrDataProcessActivator(assetResolver);
        }

        public IntPtr CreateClrDataProcess(IRuntime runtime)
        {
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            ICLRDebugging clrDebugging = GetClrDebugging();
            if (clrDebugging is null)
            {
                // dbgshim is not available in this host; the caller falls back to loading the DAC.
                return IntPtr.Zero;
            }

            DataTargetWrapper dataTarget;
            lock (_lock)
            {
                dataTarget = new DataTargetWrapper(runtime.Services, runtime);
                _dataTargets.Add(dataTarget);
            }

            ClrDebuggingVersion maxDebuggerSupportedVersion = new()
            {
                StructVersion = 0,
                Major = 4,
                Minor = 0,
                Build = 0,
                Revision = 0,
            };

            Guid riidProcess = RuntimeWrapper.IID_IXCLRDataProcess;
            HResult hr = clrDebugging.OpenVirtualProcess(
                runtime.RuntimeModule.ImageBase,
                dataTarget.IDataTarget,
                libraryProvider: IntPtr.Zero,
                maxDebuggerSupportedVersion,
                in riidProcess,
                out IntPtr clrDataProcess,
                out _,
                out _);

            if (!hr || clrDataProcess == IntPtr.Zero)
            {
                Trace.TraceInformation($"ClrDataProcessActivator: dbgshim declined runtime #{runtime.Id} (hr={hr:x8}); falling back to DAC load.");
                if (clrDataProcess != IntPtr.Zero)
                {
                    COMHelper.Release(clrDataProcess);
                }
                return IntPtr.Zero;
            }

            Trace.TraceInformation($"ClrDataProcessActivator: activated IXCLRDataProcess for runtime #{runtime.Id} via dbgshim (cDAC preferred).");
            return clrDataProcess;
        }

        private ICLRDebugging GetClrDebugging()
        {
            lock (_lock)
            {
                if (_initialized)
                {
                    return _clrDebugging;
                }
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

                    CLRCreateInstanceDelegate clrCreateInstance =
                        (CLRCreateInstanceDelegate)Marshal.GetDelegateForFunctionPointer(createInstance, typeof(CLRCreateInstanceDelegate));
                    HResult hr = clrCreateInstance(ICLRDebugging.CLSID_ICLRDebugging, ICLRDebugging.IID_ICLRDebugging, out IntPtr punk);
                    if (!hr || punk == IntPtr.Zero)
                    {
                        Trace.TraceError($"ClrDataProcessActivator: CLRCreateInstance failed (hr={hr:x8}).");
                        return null;
                    }

                    ICLRDebugging clrDebugging = ICLRDebugging.Create(punk);

                    // The prefer-cDAC policy is a stable per-instance choice: a serviceable target is
                    // opened by the co-located cDAC, and an unserviceable one is declined so the caller
                    // can fall back to the DAC.
                    if (COMHelper.QueryInterface(clrDebugging.InterfacePointer, ICLRDebuggingPolicy.IID_ICLRDebuggingPolicy, out IntPtr policyPtr))
                    {
                        ICLRDebuggingPolicy policy = ICLRDebuggingPolicy.Create(policyPtr);
                        policy.SetCDacLoadPolicy(DbgShimCDacLoadPolicy.PreferCDac);
                        COMHelper.Release(policyPtr);
                    }

                    _clrDebugging = clrDebugging;
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    Trace.TraceError($"ClrDataProcessActivator: failed to load dbgshim: {ex.Message}");
                    _clrDebugging = null;
                }

                return _clrDebugging;
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
