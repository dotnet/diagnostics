// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime instance implementation
    /// </summary>
    public class Runtime : IRuntime, IDisposable
    {
        private readonly ClrInfo _clrInfo;
        private readonly ISettingsService _settingsService;
        private readonly ISymbolService _symbolService;
        private Version _runtimeVersion;
        private ClrRuntime _clrRuntime;
        // Host-owned reference retained for the registered ClrInfo lifetime.
        private IntPtr _clrDataProcess;
        private string _dacFilePath;
        private bool _verifySignature;
        private string _dbiFilePath;

        protected readonly ServiceContainer _serviceContainer;

        public Runtime(IServiceProvider services, int id, ClrInfo clrInfo)
        {
            Target = services.GetService<ITarget>() ?? throw new DiagnosticsException("Dump or live session target required");
            Id = id;
            _clrInfo = clrInfo ?? throw new ArgumentNullException(nameof(clrInfo));
            _settingsService = services.GetService<ISettingsService>() ?? throw new ArgumentException("ISettingsService required");
            _symbolService = services.GetService<ISymbolService>() ?? throw new ArgumentException("ISymbolService required");

            RuntimeType = GetRuntimeType(clrInfo.Flavor);
            RuntimeModule = services.GetService<IModuleService>().GetModuleFromBaseAddress(clrInfo.ModuleInfo.ImageBase);

            ServiceContainerFactory containerFactory = services.GetService<IServiceManager>().CreateServiceContainerFactory(ServiceScope.Runtime, services);
            containerFactory.AddServiceFactory<ClrRuntime>((services) => CreateRuntime());
            _serviceContainer = containerFactory.Build();
            _serviceContainer.AddService<IRuntime>(this);
            _serviceContainer.AddService(clrInfo);

            Trace.TraceInformation($"Created runtime #{id} {clrInfo.Flavor} {clrInfo}");
        }

        void IDisposable.Dispose()
        {
            // The DataTarget created in the RuntimeProvider is disposed here. The ClrRuntime
            // instance is disposed below in DisposeServices().
            _clrRuntime?.DataTarget.Dispose();
            _clrRuntime = null;
            _serviceContainer.RemoveService(typeof(IRuntime));
            _serviceContainer.DisposeServices();
            if (_clrDataProcess != IntPtr.Zero)
            {
                Marshal.Release(_clrDataProcess);
                _clrDataProcess = IntPtr.Zero;
            }
        }

        #region IRuntime

        public int Id { get; }

        public ITarget Target { get; }

        public IServiceProvider Services => _serviceContainer;

        public RuntimeType RuntimeType { get; }

        public IModule RuntimeModule { get; }

        public string RuntimeModuleDirectory { get; set; }

        public Version RuntimeVersion
        {
            get
            {
                if (_runtimeVersion is null)
                {
                    Version version = _clrInfo.Version;
                    if (version is null || version.Equals(Utilities.EmptyVersion))
                    {
                        version = Utilities.ParseVersionString(RuntimeModule.GetVersionString());
                    }
                    _runtimeVersion = version;
                }
                return _runtimeVersion;
            }
        }

        public string GetDacFilePath(out bool verifySignature)
        {
            if (_dacFilePath is null)
            {
                _dacFilePath = GetLibraryPath(DebugLibraryKind.Dac);
                if (_dacFilePath is not null)
                {
                    _verifySignature = _settingsService.DacSignatureVerificationEnabled;
                }
            }
            verifySignature = _verifySignature;
            return _dacFilePath;
        }

        public string GetDbiFilePath()
        {
            _dbiFilePath ??= GetLibraryPath(DebugLibraryKind.Dbi);
            return _dbiFilePath;
        }

        #endregion

        /// <summary>
        /// Returns whether cDAC activation is enabled for this runtime.
        /// </summary>
        private bool ShouldUseCDac()
        {
            return _settingsService.CDacLoadPolicy switch
            {
                CDacLoadPolicy.UseLegacyDac => false,   // Never load the cDAC.
                CDacLoadPolicy.UseCDac => true,         // Always use the cDAC. Availability is checked by the caller
                                                        //  (a missing forced cDAC is a hard error).
                _ => ShouldUseCDacByDefault(),          // No explicit setting: evaluate the default policy.
            };
        }

        /// <summary>
        /// The default cDAC policy used when <see cref="ISettingsService.CDacLoadPolicy"/> is not set.
        /// </summary>
        private static bool ShouldUseCDacByDefault()
        {
            // When DOTNET_ENABLE_CDAC is requested, the in-box (legacy) DAC loads and drives the
            // cDAC contract reader itself, including its own dac-vs-cdac fallback/comparison
            // (see CDAC_NO_FALLBACK). Defer to that mechanism rather than loading the cDAC
            // directly so those scenarios (for example, the runtime's cDAC test pipeline that
            // points at a freshly built cDAC via -liveruntimedir) keep working.
            if (Environment.GetEnvironmentVariable("DOTNET_ENABLE_CDAC") == "1"
               || Environment.GetEnvironmentVariable("COMPlus_ENABLE_CDAC") == "1")
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Create ClrRuntime instance
        /// </summary>
        private ClrRuntime CreateRuntime()
        {
            CDacLoadPolicy policy = _settingsService.CDacLoadPolicy;
            Trace.TraceInformation($"Runtime #{Id} data-access: begin (cDAC policy={policy}, cDAC selected={ShouldUseCDac()})");

            if (ShouldUseCDac())
            {
                IClrDataProcessActivator activator = Services.GetService<IClrDataProcessActivator>();
                if (activator is not null)
                {
                    Trace.TraceInformation($"Runtime #{Id} data-access: requesting an IXCLRDataProcess (cDAC preferred)");
                    IntPtr clrDataProcess = activator.CreateClrDataProcess(this, policy);
                    if (clrDataProcess != IntPtr.Zero)
                    {
                        Trace.TraceInformation($"Runtime #{Id} data-access: received an IXCLRDataProcess");
                        return CreateRuntimeFromClrDataProcess(clrDataProcess);
                    }
                    Trace.TraceInformation($"Runtime #{Id} data-access: no IXCLRDataProcess was created");
                }
                else
                {
                    Trace.TraceInformation($"Runtime #{Id} data-access: no IXCLRDataProcess activator is registered");
                }
            }

            if (policy == CDacLoadPolicy.UseCDac)
            {
                Trace.TraceError($"Runtime #{Id} data-access: cDAC was forced (UseCDac) but could not service this runtime; not falling back to the DAC: {RuntimeModule.FileName}");
                return null;
            }

            // We ignore the dac verification param since it's already set as part of the CLRMD DataTarget creation
            // now (it's a global setting to the session).
            string dacFilePath = GetDacFilePath(out _);
            if (dacFilePath is not null)
            {
                Trace.TraceInformation($"Runtime #{Id} data-access: falling back to the in-box DAC {dacFilePath}");
                return TryCreateRuntimeFromDac(dacFilePath);
            }

            Trace.TraceError($"Runtime #{Id} data-access: could not find or download a matching DAC for this runtime: {RuntimeModule.FileName}");
            return null;
        }

        /// <summary>
        /// Creates a ClrRuntime with the specified DAC.
        /// </summary>
        private ClrRuntime TryCreateRuntimeFromDac(string dacFilePath)
        {
            Trace.TraceInformation($"Creating ClrRuntime #{Id} {dacFilePath}");
            try
            {
                // Ignore the DAC version mismatch that can happen because the clrmd ELF dump reader
                // returns 0.0.0.0 for the runtime module that the DAC is matched against.
                return _clrRuntime = _clrInfo.CreateRuntime(dacFilePath, ignoreMismatch: true);
            }
            catch (Exception ex) when
               (ex is DllNotFoundException or
                FileNotFoundException or
                InvalidOperationException or
                InvalidDataException or
                ClrDiagnosticsException)
            {
                Trace.TraceError("CreateRuntime FAILED: {0}", ex.ToString());
                return null;
            }
        }

        /// <summary>
        /// Creates a ClrRuntime with the supplied IXCLRDataProcess.
        /// </summary>
        private ClrRuntime CreateRuntimeFromClrDataProcess(IntPtr clrDataProcess)
        {
            bool registered = false;
            try
            {
                _clrDataProcess = clrDataProcess;
                _clrInfo.DataTarget.AddLoadedRuntime(_clrInfo, clrDataProcess);
                registered = true;
                Trace.TraceInformation($"Creating ClrRuntime #{Id} from IXCLRDataProcess");
                return _clrRuntime = _clrInfo.CreateRuntime();
            }
            catch (Exception ex) when
               (ex is DllNotFoundException or
                FileNotFoundException or
                InvalidOperationException or
                InvalidDataException or
                ClrDiagnosticsException)
            {
                Trace.TraceError("CreateRuntime from IXCLRDataProcess FAILED: {0}", ex.ToString());
                return null;
            }
            finally
            {
                if (!registered)
                {
                    Marshal.Release(clrDataProcess);
                    _clrDataProcess = IntPtr.Zero;
                }
            }
        }

        private string GetLibraryPath(DebugLibraryKind kind)
        {
            Architecture currentArch = RuntimeInformation.ProcessArchitecture;
            string libraryPath = null;

            foreach (DebugLibraryInfo libraryInfo in _clrInfo.DebuggingLibraries)
            {
                if (libraryInfo.Kind == kind && RuntimeInformation.IsOSPlatform(libraryInfo.Platform) && libraryInfo.TargetArchitecture == currentArch)
                {
                    libraryPath = GetLocalPath(libraryInfo);
                    if (libraryPath is not null)
                    {
                        break;
                    }
                    if (libraryInfo.ArchivedUnder != SymbolProperties.None)
                    {
                        libraryPath = DownloadFile(libraryInfo);
                        if (libraryPath is not null)
                        {
                            break;
                        }
                    }
                }
            }

            return libraryPath;
        }

        private string GetLocalPath(DebugLibraryInfo libraryInfo)
        {
            string localFilePath;
            if (!string.IsNullOrEmpty(RuntimeModuleDirectory))
            {
                localFilePath = Path.Combine(RuntimeModuleDirectory, Path.GetFileName(libraryInfo.FileName));
            }
            else
            {
                localFilePath = Path.Combine(Path.GetDirectoryName(RuntimeModule.FileName), Path.GetFileName(libraryInfo.FileName));
            }
            if (localFilePath is null || !File.Exists(localFilePath))
            {
                localFilePath = null;
            }
            return localFilePath;
        }

        private string DownloadFile(DebugLibraryInfo libraryInfo)
        {
            OSPlatform platform = Target.OperatingSystem;
            string filePath = null;

            if (_symbolService.IsSymbolStoreEnabled)
            {
                SymbolStoreKey key = null;

                if (platform == OSPlatform.Windows)
                {
                    // It is the coreclr.dll's id (timestamp/filesize) in the DacInfo used to download the the dac module.
                    if (libraryInfo.IndexTimeStamp != 0 && libraryInfo.IndexFileSize != 0)
                    {
                        key = PEFileKeyGenerator.GetKey(libraryInfo.FileName, (uint)libraryInfo.IndexTimeStamp, (uint)libraryInfo.IndexFileSize);
                    }
                    else
                    {
                        Trace.TraceError($"DownloadFile: {libraryInfo}: key not generated - no index timestamp/filesize");
                    }
                }
                else
                {
                    // Use the runtime's build id to download the the dac module.
                    if (!libraryInfo.IndexBuildId.IsDefaultOrEmpty)
                    {
                        byte[] buildId = libraryInfo.IndexBuildId.ToArray();
                        IEnumerable<SymbolStoreKey> keys = null;
                        KeyTypeFlags flags = KeyTypeFlags.None;
                        string fileName = null;

                        switch (libraryInfo.ArchivedUnder)
                        {
                            case SymbolProperties.Self:
                                flags = KeyTypeFlags.IdentityKey;
                                fileName = libraryInfo.FileName;
                                break;
                            case SymbolProperties.Coreclr:
                                flags = KeyTypeFlags.DacDbiKeys;
                                break;
                        }

                        if (platform == OSPlatform.Linux)
                        {
                            keys = ELFFileKeyGenerator.GetKeys(flags, fileName ?? "libcoreclr.so", buildId, symbolFile: false, symbolFileName: null);
                        }
                        else if (platform == OSPlatform.OSX)
                        {
                            keys = MachOFileKeyGenerator.GetKeys(flags, fileName ?? "libcoreclr.dylib", buildId, symbolFile: false, symbolFileName: null);
                        }
                        else
                        {
                            Trace.TraceError($"DownloadFile: {libraryInfo}: platform not supported - {platform}");
                        }

                        key = keys?.SingleOrDefault((k) => Path.GetFileName(k.FullPathName) == Path.GetFileName(libraryInfo.FileName));
                    }
                    else
                    {
                        Trace.TraceError($"DownloadFile: {libraryInfo}: key not generated - no index time stamp or file size");
                    }
                }

                if (key is not null)
                {
                    // Now download the DAC module from the symbol server
                    filePath = _symbolService.DownloadFile(key.Index, key.FullPathName);
                }
            }
            else
            {
                Trace.TraceInformation($"DownLoadFile: {libraryInfo}: symbol store not enabled");
            }
            return filePath;
        }

        public override bool Equals(object obj)
        {
            IRuntime runtime = (IRuntime)obj;
            return Target == runtime.Target && Id == runtime.Id;
        }

        public override int GetHashCode()
        {
            return Utilities.CombineHashCodes(Target.GetHashCode(), Id.GetHashCode());
        }

        private static readonly string[] s_runtimeTypeNames = {
            "Unknown",
            "Desktop .NET Framework",
            ".NET Core",
            ".NET Core (single-file)",
            "Native AOT",
            "Other"
        };

        private static RuntimeType GetRuntimeType(ClrFlavor flavor) => flavor switch
        {
            ClrFlavor.Core => RuntimeType.NetCore,
            ClrFlavor.Desktop => RuntimeType.Desktop,
            ClrFlavor.NativeAOT => RuntimeType.NativeAOT,
            _ => RuntimeType.Unknown,
        };

        public override string ToString()
        {
            StringBuilder sb = new();
            string config = s_runtimeTypeNames[(int)RuntimeType];
            string index = _clrInfo.BuildId.IsDefaultOrEmpty ? $"{_clrInfo.IndexTimeStamp:X8} {_clrInfo.IndexFileSize:X8}" : _clrInfo.BuildId.ToHex();
            sb.AppendLine($"#{Id} {config} runtime {_clrInfo} at {RuntimeModule.ImageBase:X16} size {RuntimeModule.ImageSize:X8} index {index}");
            if (_clrInfo.IsSingleFile)
            {
                sb.Append($"    Single-file runtime module path: {RuntimeModule.FileName}");
            }
            else
            {
                sb.Append($"    Runtime module path: {RuntimeModule.FileName}");
            }
            if (RuntimeModuleDirectory is not null)
            {
                sb.AppendLine();
                sb.Append($"    Runtime module directory: {RuntimeModuleDirectory}");
            }
            if (_dacFilePath is not null)
            {
                sb.AppendLine();
                string verify = _verifySignature ? "(verify)" : "(don't verify)";
                sb.Append($"    DAC: {_dacFilePath} {verify}");
            }
            if (_dbiFilePath is not null)
            {
                sb.AppendLine();
                sb.Append($"    DBI: {_dbiFilePath}");
            }
            return sb.ToString();
        }
    }
}
