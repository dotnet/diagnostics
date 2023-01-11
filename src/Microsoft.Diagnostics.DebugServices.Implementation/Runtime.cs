// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime instance implementation
    /// </summary>
    public class Runtime : IRuntime, IDisposable
    {
        private readonly ClrInfo _clrInfo;
        private readonly IDisposable _onFlushEvent;
        private readonly ISymbolService _symbolService;
        private string _dacFilePath;
        private string _dbiFilePath;

        protected readonly ServiceContainer _serviceContainer;

        public Runtime(IServiceProvider services, int id, ClrInfo clrInfo)
        {
            Target = services.GetService<ITarget>() ?? throw new ArgumentNullException();
            Id = id;
            _clrInfo = clrInfo ?? throw new ArgumentNullException(nameof(clrInfo));
            _symbolService = services.GetService<ISymbolService>();

            RuntimeType = RuntimeType.Unknown;
            if (clrInfo.Flavor == ClrFlavor.Core) {
                RuntimeType = RuntimeType.NetCore;
            }
            else if (clrInfo.Flavor == ClrFlavor.Desktop) {
                RuntimeType = RuntimeType.Desktop;
            }
            RuntimeModule = services.GetService<IModuleService>().GetModuleFromBaseAddress(clrInfo.ModuleInfo.ImageBase);

            ServiceContainerFactory containerFactory = services.GetService<IServiceManager>().CreateServiceContainerFactory(ServiceScope.Runtime, services);
            containerFactory .AddServiceFactory<ClrRuntime>((services) => CreateRuntime());
            _serviceContainer = containerFactory.Build();
            _serviceContainer.AddService<IRuntime>(this);
            _serviceContainer.AddService<ClrInfo>(clrInfo);

            _onFlushEvent = Target.OnFlushEvent.Register(Flush);

            Trace.TraceInformation($"Created runtime #{id} {clrInfo.Flavor} {clrInfo}");
        }

        void IDisposable.Dispose()
        {
            _serviceContainer.RemoveService(typeof(IRuntime));
            _serviceContainer.DisposeServices();
            _onFlushEvent.Dispose();
        }

        private void Flush()
        {
            if (_serviceContainer.TryGetCachedService(typeof(ClrRuntime), out object service))
            {
                ((ClrRuntime)service).FlushCachedData();
            }
        }

        #region IRuntime

        public int Id { get; }

        public ITarget Target { get; }

        public IServiceProvider Services => _serviceContainer;

        public RuntimeType RuntimeType { get; }

        public IModule RuntimeModule { get; }

        public string RuntimeModuleDirectory { get; set; }

        public string GetDacFilePath()
        {
            if (_dacFilePath is null)
            {
                _dacFilePath = GetLibraryPath(DebugLibraryKind.Dac);
            }
            return _dacFilePath;
        }

        public string GetDbiFilePath()
        {
            if (_dbiFilePath is null)
            {
                _dbiFilePath = GetLibraryPath(DebugLibraryKind.Dbi);
            }
            return _dbiFilePath;
        }

        #endregion

        /// <summary>
        /// Create ClrRuntime instance
        /// </summary>
        private ClrRuntime CreateRuntime()
        {
            string dacFilePath = GetDacFilePath();
            if (dacFilePath is not null)
            {
                Trace.TraceInformation($"Creating ClrRuntime #{Id} {dacFilePath}");
                try
                {
                    // Ignore the DAC version mismatch that can happen because the clrmd ELF dump reader 
                    // returns 0.0.0.0 for the runtime module that the DAC is matched against.
                    return _clrInfo.CreateRuntime(dacFilePath, ignoreMismatch: true);
                }
                catch (Exception ex) when
                   (ex is DllNotFoundException || 
                    ex is FileNotFoundException || 
                    ex is InvalidOperationException || 
                    ex is InvalidDataException || 
                    ex is ClrDiagnosticsException)
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

        private string GetLibraryPath(DebugLibraryKind kind)
        {
            Architecture currentArch = RuntimeInformation.ProcessArchitecture;
            string libraryPath = null;

            foreach (DebugLibraryInfo libraryInfo in _clrInfo.DebuggingLibraries)
            {
                if (libraryInfo.Kind == kind && RuntimeInformation.IsOSPlatform(libraryInfo.Platform) && libraryInfo.TargetArchitecture == currentArch)
                {
                    libraryPath = GetLocalPath(libraryInfo.FileName);
                    if (libraryPath is not null)
                    {
                        break;
                    }
                    libraryPath = DownloadFile(libraryInfo);
                    if (libraryPath is not null)
                    {
                        break;
                    }
                }
            }

            return libraryPath;
        }

        private string GetLocalPath(string fileName)
        {
            if (File.Exists(fileName))
            {
                return fileName;
            }
            string localFilePath;
            if (!string.IsNullOrEmpty(RuntimeModuleDirectory))
            {
                localFilePath = Path.Combine(RuntimeModuleDirectory, Path.GetFileName(fileName));
            }
            else
            {
                localFilePath = Path.Combine(Path.GetDirectoryName(RuntimeModule.FileName), Path.GetFileName(fileName));
            }
            if (!File.Exists(localFilePath))
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
                    filePath = _symbolService.DownloadFile(key);
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
            "Other"
        };

        public override string ToString()
        {
            var sb = new StringBuilder();
            string config = s_runtimeTypeNames[(int)RuntimeType];
            string index = _clrInfo.BuildId.IsDefaultOrEmpty ? $"{_clrInfo.IndexTimeStamp:X8} {_clrInfo.IndexFileSize:X8}" : _clrInfo.BuildId.ToHex();
            sb.AppendLine($"#{Id} {config} runtime {_clrInfo} at {RuntimeModule.ImageBase:X16} size {RuntimeModule.ImageSize:X8} index {index}");
            if (_clrInfo.IsSingleFile) {
                sb.Append($"    Single-file runtime module path: {RuntimeModule.FileName}");
            }
            else {
                sb.Append($"    Runtime module path: {RuntimeModule.FileName}");
            }
            if (RuntimeModuleDirectory is not null) {
                sb.AppendLine();
                sb.Append($"    Runtime module directory: {RuntimeModuleDirectory}");
            }
            if (_dacFilePath is not null) {
                sb.AppendLine();
                sb.Append($"    DAC: {_dacFilePath}");
            }
            if (_dbiFilePath is not null) {
                sb.AppendLine();
                sb.Append($"    DBI: {_dbiFilePath}");
            }
            return sb.ToString();
        }
    }
}
