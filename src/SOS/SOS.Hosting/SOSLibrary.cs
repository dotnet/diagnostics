// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SOS.Hosting
{
    /// <summary>
    /// Helper code to load and initialize SOS
    /// </summary>
    public sealed class SOSLibrary
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SOSCommandDelegate(
            IntPtr ILLDBServices,
            [In, MarshalAs(UnmanagedType.LPStr)] string args);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SOSInitializeDelegate(
            IntPtr IHost,
            IntPtr IDebuggerServices);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void SOSUninitializeDelegate();

        private const string SOSInitialize = "SOSInitializeByHost";
        private const string SOSUninitialize = "SOSUninitializeByHost";

        private readonly IContextService _contextService;
        private readonly HostWrapper _hostWrapper;
        private IntPtr _sosLibrary = IntPtr.Zero;

        /// <summary>
        /// The native SOS binaries path. Default is OS/architecture (RID) named directory in the same directory as this assembly.
        /// </summary>
        public string SOSPath { get; set; }

        public static SOSLibrary Create(IHost host)
        {
            SOSLibrary sosLibrary = null;
            try
            {
                sosLibrary = new SOSLibrary(host);
                sosLibrary.Initialize();
            }
            catch
            {
                sosLibrary.Uninitialize();
                sosLibrary = null;
                throw;
            }
            host.OnShutdownEvent.Register(() => {
                sosLibrary.Uninitialize();
                sosLibrary = null;
            });
            return sosLibrary;
        }

        /// <summary>
        /// Create an instance of the hosting class
        /// </summary>
        /// <param name="target">target instance</param>
        private SOSLibrary(IHost host)
        {
            _contextService = host.Services.GetService<IContextService>();

            string rid = InstallHelper.GetRid();
            SOSPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), rid);

            _hostWrapper = new HostWrapper(host, () => GetSOSHost()?.TargetWrapper);
        }

        /// <summary>
        /// Loads and initializes the SOS module.
        /// </summary>
        private void Initialize()
        {
            if (_sosLibrary == IntPtr.Zero)
            {
                string sos;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    sos = "sos.dll";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    sos = "libsos.so";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    sos = "libsos.dylib";
                }
                else {
                    throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                }
                string sosPath = Path.Combine(SOSPath, sos);
                try
                {
                    _sosLibrary = Microsoft.Diagnostics.Runtime.DataTarget.PlatformFunctions.LoadLibrary(sosPath);
                }
                catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException)
                {
                    // This is a workaround for the Microsoft SDK docker images. Can fail when LoadLibrary uses libdl.so to load the SOS module.
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        throw new DllNotFoundException($"Problem loading SOS module from {sosPath}. Try installing libc6-dev (apt-get install libc6-dev) to work around this problem.", ex);
                    }
                    else
                    {
                        throw new DllNotFoundException($"Problem loading SOS module from {sosPath}", ex);
                    }
                }
                Debug.Assert(_sosLibrary != IntPtr.Zero);
                var initializeFunc = SOSHost.GetDelegateFunction<SOSInitializeDelegate>(_sosLibrary, SOSInitialize);
                if (initializeFunc == null)
                {
                    throw new EntryPointNotFoundException($"Can not find SOS module initialization function: {SOSInitialize}");
                }
                int result = initializeFunc(_hostWrapper.IHost, IntPtr.Zero);
                if (result != 0)
                {
                    throw new InvalidOperationException($"SOS initialization FAILED 0x{result:X8}");
                }
                Trace.TraceInformation("SOS initialized: sosPath '{0}'", sosPath);
            }
        }

        /// <summary>
        /// Shutdown/clean up the native SOS module.
        /// </summary>
        private void Uninitialize()
        {
            Trace.TraceInformation("SOSHost: Uninitialize");
            if (_sosLibrary != IntPtr.Zero)
            {
                var uninitializeFunc = SOSHost.GetDelegateFunction<SOSUninitializeDelegate>(_sosLibrary, SOSUninitialize);
                uninitializeFunc?.Invoke();

                Microsoft.Diagnostics.Runtime.DataTarget.PlatformFunctions.FreeLibrary(_sosLibrary);
                _sosLibrary = IntPtr.Zero;
            }
            _hostWrapper.Release();
        }

        /// <summary>
        /// Execute a SOS command.
        /// </summary>
        /// <param name="client">client interface</param>
        /// <param name="command">just the command name</param>
        /// <param name="arguments">the command arguments and options</param>
        public void ExecuteCommand(IntPtr client, string command, string arguments)
        {
            Debug.Assert(_sosLibrary != IntPtr.Zero);

            var commandFunc = SOSHost.GetDelegateFunction<SOSCommandDelegate>(_sosLibrary, command);
            if (commandFunc == null)
            {
                throw new DiagnosticsException($"SOS command not found: {command}");
            }
            int result = commandFunc(client, arguments ?? "");
            if (result != HResult.S_OK)
            {
                Trace.TraceError($"SOS command FAILED 0x{result:X8}");
            }
        }

        private SOSHost GetSOSHost() => _contextService.Services.GetService<SOSHost>();
    }
}
