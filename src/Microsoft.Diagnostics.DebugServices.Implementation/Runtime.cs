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
        private readonly IHostAssetResolver _hostAssetResolver;
        private readonly ISettingsService _settingsService;
        private readonly ISymbolService _symbolService;
        private readonly IConsoleService _consoleService;
        private Version _runtimeVersion;
        private ClrRuntime _clrRuntime;
        private string _dacFilePath;
        private string _cdacFilePath;
        private string _dbiFilePath;

        protected readonly ServiceContainer _serviceContainer;

        public Runtime(IServiceProvider services, int id, ClrInfo clrInfo)
        {
            Target = services.GetService<ITarget>() ?? throw new DiagnosticsException("Dump or live session target required");
            Id = id;
            _clrInfo = clrInfo ?? throw new ArgumentNullException(nameof(clrInfo));
            // IHostAssetResolver is optional: it is registered by the SOS hosting layer to locate
            // the bundled cDAC. When absent (hosts without SOS.Hosting, e.g. some test hosts), cDAC
            // resolution returns null and the in-box DAC is used.
            _hostAssetResolver = services.GetService<IHostAssetResolver>();
            _settingsService = services.GetService<ISettingsService>() ?? throw new ArgumentException("ISettingsService required");
            _symbolService = services.GetService<ISymbolService>() ?? throw new ArgumentException("ISymbolService required");
            // IConsoleService is optional: when present it is used to surface actionable guidance
            // (for example, to run 'setclrpath') when the DAC/DBI cannot be found or downloaded.
            _consoleService = services.GetService<IConsoleService>();

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
                _dacFilePath = GetLibraryPath(DebugLibraryKind.Dac, allowDownload: DownloadAllowed);
                if (_dacFilePath is null)
                {
                    WriteDebugLibraryNotFoundWarning(DebugLibraryKind.Dac);
                }
            }
            verifySignature = VerifyDebugLibrarySignature(_dacFilePath);
            return _dacFilePath;
        }

        public string GetCDacFilePath()
        {
            // ShouldUseCDac() evaluates the cDAC loading policy. When it returns false the caller
            // uses the in-box DAC from GetDacFilePath instead.
            if (!ShouldUseCDac())
            {
                return null;
            }

            // The cDAC is bundled with the diagnostics tool and is never downloaded, so a missing
            // path means it isn't available for this host.
            _cdacFilePath ??= GetLibraryPath(DebugLibraryKind.CDac, allowDownload: false);
            if (_cdacFilePath is null && _settingsService.CDacLoadPolicy == CDacLoadPolicy.UseCDac)
            {
                // The cDAC was explicitly forced but isn't bundled with this tool.
                throw new DiagnosticsException($"The cDAC was explicitly requested but no matching cDAC is available for this runtime: {RuntimeModule.FileName}");
            }
            return _cdacFilePath;
        }

        public string GetDbiFilePath(out bool verifySignature)
        {
            if (_dbiFilePath is null)
            {
                _dbiFilePath = GetLibraryPath(DebugLibraryKind.Dbi, allowDownload: DownloadAllowed);
                if (_dbiFilePath is null)
                {
                    WriteDebugLibraryNotFoundWarning(DebugLibraryKind.Dbi);
                }
            }
            verifySignature = VerifyDebugLibrarySignature(_dbiFilePath);
            return _dbiFilePath;
        }

        /// <summary>
        /// The DAC and DBI are both verified according to the single DacSignatureVerificationEnabled
        /// setting; there is no path where one is verified and the other is not. The cDAC is the only
        /// debugging library that is never verified, and it is loaded through a separate path.
        /// </summary>
        private bool VerifyDebugLibrarySignature(string libraryPath) =>
            libraryPath is not null && _settingsService.DacSignatureVerificationEnabled;

        #endregion

        /// <summary>
        /// The minimum runtime major version that supports the cDAC.
        /// </summary>
        private const int MinCDacRuntimeMajorVersion = 11;

        /// <summary>
        /// Evaluates the cDAC loading policy for this runtime. This is the single place that
        /// decides whether the diagnostics tool should load the cDAC itself in place of the
        /// in-box DAC, based on the <see cref="ISettingsService.CDacLoadPolicy"/> setting and the
        /// target runtime version.
        /// </summary>
        private bool ShouldUseCDac()
        {
            return _settingsService.CDacLoadPolicy switch
            {
                CDacLoadPolicy.UseLegacyDac => false,   // Never load the cDAC.
                CDacLoadPolicy.UseCDac => true,         // Always use the cDAC, regardless of the runtime version. Availability is
                                                        //  checked by the caller (a missing forced cDAC is a hard error).
                _ => ShouldUseCDacByDefault(),          // No explicit setting: evaluate the default policy.
            };
        }

        /// <summary>
        /// The default cDAC policy used when <see cref="ISettingsService.CDacLoadPolicy"/> is not set.
        /// </summary>
        private bool ShouldUseCDacByDefault()
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

            // Default policy: use the cDAC only for runtimes that support it. This needs to be
            //  changed to consider native AOT and singlefile. This is a dummy policy for work
            //  we will offload to dbgshim.
            return RuntimeVersion is not null && RuntimeVersion.Major >= MinCDacRuntimeMajorVersion;
        }

        /// <summary>
        /// Create ClrRuntime instance
        /// </summary>
        private ClrRuntime CreateRuntime()
        {
            // Prefer the cDAC for the ClrMD data-access path when policy selects it; fall back to the in-box DAC.
            // We ignore the dac verification param since it's already set as part of the CLRMD DataTarget creation
            // now (it's a global setting to the session).
            string dacFilePath = GetCDacFilePath() ?? GetDacFilePath(out _);
            if (dacFilePath is not null)
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
                }
            }
            else
            {
                Trace.TraceError($"Could not find or download matching DAC for this runtime: {RuntimeModule.FileName}");
            }
            return null;
        }

        private string GetLibraryPath(DebugLibraryKind kind, bool allowDownload)
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
                    // The cDAC is an analyzer-host artifact shipped inside the diagnostics tool
                    // (next to sos.dll, matching the host's RID). It is not symbol-store indexed
                    // by the target runtime, so never attempt to download it.
                    if (libraryInfo.Kind == DebugLibraryKind.CDac)
                    {
                        continue;
                    }
                    if (libraryInfo.ArchivedUnder != SymbolProperties.None && allowDownload)
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

        /// <summary>
        /// Symbol-server download of a DAC/DBI is only permitted when the downloaded binary will be
        /// authenticode-verified before it is loaded and run. That requires a Windows host (authenticode
        /// verification is Windows-only, and a non-Windows host cannot load a foreign-format PE DAC/DBI
        /// anyway) AND that verification has not been disabled. The DacSignatureVerification override only
        /// relaxes verification for locally-provided DAC/DBI (see 'setclrpath'); it must never allow a
        /// remotely-acquired, unauthenticated binary to be loaded. When download is not permitted the
        /// matching DAC/DBI must be provided locally.
        /// </summary>
        private bool DownloadAllowed =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _settingsService.DacSignatureVerificationEnabled;

        private void WriteDebugLibraryNotFoundWarning(DebugLibraryKind kind)
        {
            if (DownloadAllowed)
            {
                return;
            }
            string library = kind == DebugLibraryKind.Dbi ? "DBI" : "DAC";
            _consoleService?.WriteWarning(
                $"Could not find matching {library} for runtime: {RuntimeModule.FileName}{Environment.NewLine}" +
                $"Downloading debugging libraries from the symbol server is only supported on Windows with DAC signature verification enabled.{Environment.NewLine}" +
                $"Use 'setclrpath <directory>' to point at the directory that contains the matching DAC/DBI files{Environment.NewLine}" +
                $"(for example the runtime's shared framework directory). See 'soshelp setclrpath' for more information.{Environment.NewLine}");
        }

        private string GetLocalPath(DebugLibraryInfo libraryInfo)
        {
            string localFilePath;
            if (libraryInfo.Kind == DebugLibraryKind.CDac)
            {
                // The cDAC ships next to the native sos module. Ask the host asset resolver where it
                // is rather than reasoning about layouts here (ClrMD's DebuggingLibraries entry points
                // at the managed-assembly base directory, so it is ignored). The shared existence
                // check below verifies the path, so the in-box DAC is used when the cDAC isn't bundled.
                localFilePath = _hostAssetResolver?.GetCDacPath();
            }
            else
            {
                if (!string.IsNullOrEmpty(RuntimeModuleDirectory))
                {
                    localFilePath = Path.Combine(RuntimeModuleDirectory, Path.GetFileName(libraryInfo.FileName));
                }
                else
                {
                    localFilePath = Path.Combine(Path.GetDirectoryName(RuntimeModule.FileName), Path.GetFileName(libraryInfo.FileName));
                }
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
                string verify = VerifyDebugLibrarySignature(_dacFilePath) ? "(verify)" : "(don't verify)";
                sb.Append($"    DAC: {_dacFilePath} {verify}");
            }
            if (_cdacFilePath is not null)
            {
                sb.AppendLine();
                sb.Append($"    CDAC: {_cdacFilePath}");
            }
            if (_dbiFilePath is not null)
            {
                sb.AppendLine();
                string verify = VerifyDebugLibrarySignature(_dbiFilePath) ? "(verify)" : "(don't verify)";
                sb.Append($"    DBI: {_dbiFilePath} {verify}");
            }
            return sb.ToString();
        }
    }
}
