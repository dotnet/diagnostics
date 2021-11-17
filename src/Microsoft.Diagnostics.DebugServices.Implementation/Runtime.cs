// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
    /// IRuntime instance implementation
    /// </summary>
    public class Runtime : IRuntime
    {
        private readonly ClrInfo _clrInfo;
        private ISymbolService _symbolService;
        private ClrRuntime _clrRuntime;
        private string _dacFilePath;
        private string _dbiFilePath;

        public readonly ServiceProvider ServiceProvider;

        public Runtime(ITarget target, int id, ClrInfo clrInfo)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Id = id;
            _clrInfo = clrInfo ?? throw new ArgumentNullException(nameof(clrInfo));

            RuntimeType = RuntimeType.Unknown;
            if (clrInfo.Flavor == ClrFlavor.Core) {
                RuntimeType = RuntimeType.NetCore;
            }
            else if (clrInfo.Flavor == ClrFlavor.Desktop) {
                RuntimeType = RuntimeType.Desktop;
            }
            RuntimeModule = target.Services.GetService<IModuleService>().GetModuleFromBaseAddress(clrInfo.ModuleInfo.ImageBase);

            ServiceProvider = new ServiceProvider();
            ServiceProvider.AddService<ClrInfo>(clrInfo);
            ServiceProvider.AddServiceFactoryWithNoCaching<ClrRuntime>(() => CreateRuntime());

            target.OnFlushEvent.Register(() => _clrRuntime?.FlushCachedData());

            Trace.TraceInformation($"Created runtime #{id} {clrInfo.Flavor} {clrInfo}");
        }

        #region IRuntime

        public int Id { get; }

        public ITarget Target { get; }

        public IServiceProvider Services => ServiceProvider;

        public RuntimeType RuntimeType { get; }

        public IModule RuntimeModule { get; }

        public string RuntimeModuleDirectory { get; set; }

        public string GetDacFilePath()
        {
            if (_dacFilePath is null)
            {
                string dacFileName = GetDacFileName();
                _dacFilePath = GetLocalDacPath(dacFileName);
                if (_dacFilePath is null)
                {
                    _dacFilePath = DownloadFile(dacFileName);
                }
            }
            return _dacFilePath;
        }

        public string GetDbiFilePath()
        {
            if (_dbiFilePath is null)
            {
                string dbiFileName = GetDbiFileName();
                _dbiFilePath = GetLocalPath(dbiFileName);
                if (_dbiFilePath is null)
                {
                    _dbiFilePath = DownloadFile(dbiFileName);
                }
            }
            return _dbiFilePath;
        }

        #endregion

        /// <summary>
        /// Create ClrRuntime helper
        /// </summary>
        private ClrRuntime CreateRuntime()
        {
            if (_clrRuntime is null)
            {
                string dacFilePath = GetDacFilePath();
                if (dacFilePath is not null)
                {
                    Trace.TraceInformation($"Creating ClrRuntime #{Id} {dacFilePath}");
                    try
                    {
                        // Ignore the DAC version mismatch that can happen because the clrmd ELF dump reader 
                        // returns 0.0.0.0 for the runtime module that the DAC is matched against.
                        _clrRuntime = _clrInfo.CreateRuntime(dacFilePath, ignoreMismatch: true);
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
            }
            return _clrRuntime;
        }

        private string GetDacFileName()
        {
            if (_clrInfo.SingleFileRuntimeInfo.HasValue)
            {
                return ClrInfoProvider.GetDacFileName(_clrInfo.Flavor, Target.OperatingSystem);
            }
            Debug.Assert(!string.IsNullOrEmpty(_clrInfo.DacInfo.PlatformSpecificFileName));
            return _clrInfo.DacInfo.PlatformSpecificFileName;
        }

        private string GetLocalDacPath(string dacFileName)
        {
            string dacFilePath;
            if (!string.IsNullOrEmpty(RuntimeModuleDirectory))
            {
                dacFilePath = Path.Combine(RuntimeModuleDirectory, dacFileName);
            }
            else
            {
                dacFilePath = _clrInfo.DacInfo.LocalDacPath;

                // On MacOS CLRMD doesn't return the full DAC path just the file name so check if it exists
                if (string.IsNullOrEmpty(dacFilePath) || !File.Exists(dacFilePath))
                {
                    dacFilePath = Path.Combine(Path.GetDirectoryName(RuntimeModule.FileName), dacFileName);
                }
            }
            if (!File.Exists(dacFilePath))
            {
                dacFilePath = null;
            }
            return dacFilePath;
        }

        private string GetDbiFileName()
        {
            string name = Target.GetPlatformModuleName("mscordbi");

            // If this is the Linux runtime module name, but we are running on Windows return the cross-OS DBI name.
            if (Target.OperatingSystem == OSPlatform.Linux && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                name = "mscordbi.dll";
            }
            return name;
        }

        private string GetLocalPath(string fileName)
        {
            string localFilePath;
            if (!string.IsNullOrEmpty(RuntimeModuleDirectory))
            {
                localFilePath = Path.Combine(RuntimeModuleDirectory, fileName);
            }
            else
            {
                localFilePath = Path.Combine(Path.GetDirectoryName(RuntimeModule.FileName), fileName);
            }
            if (!File.Exists(localFilePath))
            {
                localFilePath = null;
            }
            return localFilePath;
        }

        private string DownloadFile(string fileName)
        {
            OSPlatform platform = Target.OperatingSystem;
            string filePath = null;

            if (SymbolService.IsSymbolStoreEnabled)
            {
                SymbolStoreKey key = null;

                if (platform == OSPlatform.Windows)
                {
                    // It is the coreclr.dll's id (timestamp/filesize) in the DacInfo used to download the the dac module.
                    if (_clrInfo.DacInfo.IndexTimeStamp != 0 && _clrInfo.DacInfo.IndexFileSize != 0)
                    {
                        key = PEFileKeyGenerator.GetKey(fileName, (uint)_clrInfo.DacInfo.IndexTimeStamp, (uint)_clrInfo.DacInfo.IndexFileSize);
                    }
                    else
                    {
                        Trace.TraceError($"DownloadFile: {fileName}: key not generated - no index timestamp/filesize");
                    }
                }
                else
                {
                    // Use the runtime's build id to download the the dac module.
                    if (!_clrInfo.DacInfo.ClrBuildId.IsDefaultOrEmpty)
                    {
                        byte[] buildId = _clrInfo.DacInfo.ClrBuildId.ToArray();
                        IEnumerable<SymbolStoreKey> keys = null;

                        if (platform == OSPlatform.Linux)
                        {
                            keys = ELFFileKeyGenerator.GetKeys(KeyTypeFlags.DacDbiKeys, "libcoreclr.so", buildId, symbolFile: false, symbolFileName: null);
                        }
                        else if (platform == OSPlatform.OSX)
                        {
                            keys = MachOFileKeyGenerator.GetKeys(KeyTypeFlags.DacDbiKeys, "libcoreclr.dylib", buildId, symbolFile: false, symbolFileName: null);
                        }
                        else
                        {
                            Trace.TraceError($"DownloadFile: {fileName}: platform not supported - {platform}");
                        }

                        key = keys?.SingleOrDefault((k) => Path.GetFileName(k.FullPathName) == fileName);
                    }
                    else
                    {
                        Trace.TraceError($"DownloadFile: {fileName}: key not generated - no index time stamp or file size");
                    }
                }

                if (key is not null)
                {
                    // Now download the DAC module from the symbol server
                    filePath = SymbolService.DownloadFile(key);
                }
            }
            else
            {
                Trace.TraceInformation($"DownLoadFile: {fileName}: symbol store not enabled");
            }
            return filePath;
        }

        private ISymbolService SymbolService => _symbolService ??= Target.Services.GetService<ISymbolService>(); 

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
            "Desktop .NET Framework",
            ".NET Core",
            ".NET Core (single-file)",
            "Unknown"
        };

        public override string ToString()
        {
            var sb = new StringBuilder();
            string config = s_runtimeTypeNames[(int)RuntimeType];
            sb.AppendLine($"#{Id} {config} runtime at {RuntimeModule.ImageBase:X16} size {RuntimeModule.ImageSize:X8}");
            if (_clrInfo.SingleFileRuntimeInfo.HasValue) {
                sb.AppendLine($"    Single-file runtime module path: {RuntimeModule.FileName}");
            }
            else {
                sb.AppendLine($"    Runtime module path: {RuntimeModule.FileName}");
            }
            if (RuntimeModuleDirectory is not null) {
                sb.AppendLine($"    Runtime module directory: {RuntimeModuleDirectory}");
            }
            if (_dacFilePath is not null) {
                sb.AppendLine($"    DAC: {_dacFilePath}");
            }
            if (_dbiFilePath is not null) {
                sb.AppendLine($"    DBI: {_dbiFilePath}");
            }
            return sb.ToString();
        }
    }
}
