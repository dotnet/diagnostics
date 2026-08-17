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

        public IntPtr CreateClrDataProcess(IRuntime runtime, CDacLoadPolicy policy)
        {
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            lock (_lock)
            {
                ICLRDebugging clrDebugging = GetClrDebugging();
                if (clrDebugging is null)
                {
                    // dbgshim is not available in this host; the caller decides whether to fall back.
                    return IntPtr.Zero;
                }

                DataTargetWrapper dataTarget = new(runtime.Services, runtime);
                _dataTargets.Add(dataTarget);

                DbgShimCDacLoadPolicy dbgShimPolicy = policy switch
                {
                    CDacLoadPolicy.UseCDac => DbgShimCDacLoadPolicy.CDacOnly,
                    CDacLoadPolicy.UseLegacyDac => DbgShimCDacLoadPolicy.LegacyDacOnly,
                    _ => DbgShimCDacLoadPolicy.PreferCDac,
                };
                if (!SetCDacLoadPolicy(clrDebugging, dbgShimPolicy))
                {
                    return IntPtr.Zero;
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
                    Trace.TraceInformation($"ClrDataProcessActivator: dbgshim declined runtime #{runtime.Id} (hr={hr:x8}, policy={dbgShimPolicy}).");
                    if (clrDataProcess != IntPtr.Zero)
                    {
                        COMHelper.Release(clrDataProcess);
                    }
                    return IntPtr.Zero;
                }

                Trace.TraceInformation($"ClrDataProcessActivator: activated IXCLRDataProcess for runtime #{runtime.Id} via dbgshim (policy={dbgShimPolicy}).");
                return clrDataProcess;
            }
        }

        private static bool SetCDacLoadPolicy(ICLRDebugging clrDebugging, DbgShimCDacLoadPolicy policy)
        {
            if (!COMHelper.QueryInterface(clrDebugging.InterfacePointer, ICLRDebuggingPolicy.IID_ICLRDebuggingPolicy, out IntPtr policyPtr))
            {
                Trace.TraceError($"ClrDataProcessActivator: dbgshim does not support ICLRDebuggingPolicy (requested policy={policy}).");
                return false;
            }

            try
            {
                HResult hr = ICLRDebuggingPolicy.Create(policyPtr).SetCDacLoadPolicy(policy);
                if (!hr)
                {
                    Trace.TraceError($"ClrDataProcessActivator: SetCDacLoadPolicy({policy}) failed (hr={hr:x8}).");
                    return false;
                }
                return true;
            }
            finally
            {
                COMHelper.Release(policyPtr);
            }
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

                    _clrDebugging = ICLRDebugging.Create(punk);
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
