// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.Diagnostics.Shared;

namespace SOS.Hosting
{
    /// <summary>
    /// Helper code to load and initialize SOS
    /// </summary>
    public sealed class SOSLibrary : IDisposable
    {
        /// <summary>
        /// Provided by a native debugger host to tell SOS hosting where the native sos module was
        /// loaded from and its handle. This is the source where sos (and the cDAC that
        /// ships next to it) comes from. When absent (in-process hosts such as dotnet-dump that load
        /// sos themselves), the directory is derived from the tool's package layout instead.
        /// </summary>
        public interface ISOSModule
        {
            /// <summary>
            /// The directory containing the native sos module (and the cDAC next to it).
            /// </summary>
            string SOSPath { get; }

            /// <summary>
            /// The native sos module handle.
            /// </summary>
            IntPtr SOSHandle { get; }
        }

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

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "FindResourceA")]
        public static extern IntPtr FindResource(IntPtr hModule, string name, string type);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResource);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LockResource(IntPtr hResource);

        private const string SOSInitialize = "SOSInitializeByHost";
        private const string SOSUninitialize = "SOSUninitializeByHost";

        private readonly HostWrapper _hostWrapper;
        private readonly bool _uninitializeLibrary;
        private IntPtr _sosLibrary = IntPtr.Zero;

        /// <summary>
        /// The native SOS binaries path. Default is OS/architecture (RID) named directory in the same directory as this assembly.
        /// </summary>
        public string SOSPath { get; set; }

        [ServiceExport(Scope = ServiceScope.Global)]
        public static SOSLibrary TryCreate(IHost host, IHostAssetResolver assetResolver, [ServiceImport(Optional = true)] ISOSModule sosModule)
        {
            SOSLibrary sosLibrary = null;
            try
            {
                sosLibrary = new SOSLibrary(host, assetResolver, sosModule);
                sosLibrary.Initialize();
            }
            catch
            {
                sosLibrary.Uninitialize();
                throw;
            }
            return sosLibrary;
        }

        /// <summary>
        /// Create an instance of the hosting class
        /// </summary>
        /// <param name="host">the host instance</param>
        /// <param name="assetResolver">resolves where the native sos binaries live (the host's sos
        /// directory, or this tool's package layout)</param>
        /// <param name="sosModule">the host-loaded sos module (handle/ownership), or null when this
        /// host loads sos itself (dotnet-dump)</param>
        private SOSLibrary(IHost host, IHostAssetResolver assetResolver, ISOSModule sosModule)
        {
            // The asset resolver is the single source of truth for the native binaries directory; it
            // already accounts for a host-supplied sos location. ISOSModule, when present, only tells
            // us the host already loaded sos so we reuse its handle instead of loading/unloading it.
            if (sosModule is not null)
            {
                SOSPath = sosModule.SOSPath;
                _sosLibrary = sosModule.SOSHandle;
            }
            else
            {
                SOSPath = assetResolver.NativeBinariesDirectory;
                _uninitializeLibrary = true;
            }
            _hostWrapper = new HostWrapper(host);
        }

        void IDisposable.Dispose() => Uninitialize();

        /// <summary>
        /// Loads and initializes the SOS module.
        /// </summary>
        private void Initialize()
        {
            if (_sosLibrary == IntPtr.Zero)
            {
                string sos;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    sos = "sos.dll";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    sos = "libsos.so";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    sos = "libsos.dylib";
                }
                else
                {
                    throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                }
                string sosPath = Path.Combine(SOSPath, sos);
                try
                {
                    _sosLibrary = Microsoft.Diagnostics.Runtime.DataTarget.PlatformFunctions.LoadLibrary(sosPath);
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
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
                SOSInitializeDelegate initializeFunc = SOSHost.GetDelegateFunction<SOSInitializeDelegate>(_sosLibrary, SOSInitialize);
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
            Trace.TraceInformation("SOSLibrary: Uninitialize");
            if (_uninitializeLibrary && _sosLibrary != IntPtr.Zero)
            {
                SOSUninitializeDelegate uninitializeFunc = SOSHost.GetDelegateFunction<SOSUninitializeDelegate>(_sosLibrary, SOSUninitialize);
                uninitializeFunc?.Invoke();

                Microsoft.Diagnostics.Runtime.DataTarget.PlatformFunctions.FreeLibrary(_sosLibrary);
            }
            _sosLibrary = IntPtr.Zero;
            _hostWrapper.ReleaseWithCheck();
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

            SOSCommandDelegate commandFunc = SOSHost.GetDelegateFunction<SOSCommandDelegate>(_sosLibrary, command);
            if (commandFunc == null)
            {
                throw new CommandNotFoundException(command);
            }
            int result = commandFunc(client, arguments ?? "");
            if (result == HResult.E_NOTIMPL)
            {
                throw new CommandNotFoundException(command);
            }
            if (result != HResult.S_OK)
            {
                Trace.TraceError($"SOS command FAILED 0x{result:X8}");
                throw new DiagnosticsException(string.Empty);
            }
        }

        public string GetHelpText(string command)
        {
            string helpText;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                IntPtr hResInfo = FindResource(_sosLibrary, "DOCUMENTATION", "TEXT");
                if (hResInfo == IntPtr.Zero)
                {
                    throw new DiagnosticsException("Can not SOS help text");
                }
                IntPtr hResource = LoadResource(_sosLibrary, hResInfo);
                if (hResource == IntPtr.Zero)
                {
                    throw new DiagnosticsException("Can not SOS help text");
                }
                IntPtr helpTextPtr = LockResource(hResource);
                if (helpTextPtr == IntPtr.Zero)
                {
                    throw new DiagnosticsException("Can not SOS help text");
                }
                helpText = Marshal.PtrToStringAnsi(helpTextPtr);
            }
            else
            {
                string helpTextFile = Path.Combine(SOSPath, "sosdocsunix.txt");
                helpText = File.ReadAllText(helpTextFile);
            }
            command = command.ToLowerInvariant();
            string searchString = $"COMMAND: {command}.";

            // Search for command in help text file
            int start = helpText.IndexOf(searchString);
            if (start == -1)
            {
                throw new DiagnosticsException($"Documentation for {command} not found");
            }

            // Go to end of line
            start = helpText.IndexOf('\n', start);
            if (start == -1)
            {
                throw new DiagnosticsException($"No newline in documentation resource or file");
            }

            // Find the first occurrence of \\ followed by an \r or an \n on a line by itself.
            int end = start++;
            while (true)
            {
                end = helpText.IndexOf("\\\\", end + 1);
                if (end == -1)
                {
                    break;
                }
                char c = helpText[end - 1];
                if (c is '\r' or '\n')
                {
                    break;
                }
                c = helpText[end + 3];
                if (c is '\r' or '\n')
                {
                    break;
                }
            }
            return end == -1 ? helpText.Substring(start) : helpText.Substring(start, end - start);
        }
    }
}
