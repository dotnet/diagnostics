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
    /// IRuntime instance implementation
    /// </summary>
    public class Runtime : IRuntime
    {
        private readonly ITarget _target;
        private readonly IRuntimeService _runtimeService;
        private readonly ClrInfo _clrInfo;
        private IMemoryService _memoryService;
        private ISymbolService _symbolService;
        private MetadataMappingMemoryService _metadataMappingMemoryService;
        private ClrRuntime _clrRuntime;
        private string _dacFilePath;
        private string _dbiFilePath;

        public readonly ServiceProvider ServiceProvider;

        public Runtime(ITarget target, IRuntimeService runtimeService, ClrInfo clrInfo, int id)
        {
            Trace.TraceInformation($"Creating runtime #{id} {clrInfo.Flavor} {clrInfo}");
            _target = target;
            _runtimeService = runtimeService;
            _clrInfo = clrInfo;
            Id = id;

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

            target.OnFlushEvent.Register(() => {
                _clrRuntime?.DacLibrary.DacPrivateInterface.Flush();
                _metadataMappingMemoryService?.Flush();
            });

            // This is a special memory service that maps the managed assemblies' metadata into
            // the address space. The lldb debugger returns zero's (instead of failing the memory
            // read) for missing pages in core dumps that older (less than 5.0) createdumps generate
            // so it needs this special metadata mapping memory service. dotnet-dump needs this logic
            // for clrstack -i (uses ICorDebug data targets).
            if (target.IsDump && 
               (target.OperatingSystem != OSPlatform.Windows) &&
               (target.Host.HostType == HostType.Lldb || 
                target.Host.HostType == HostType.DotnetDump)) 
            {
                ServiceProvider.AddServiceFactoryWithNoCaching<IMemoryService>(() => {
                    if (_metadataMappingMemoryService == null)
                    {
                        _metadataMappingMemoryService = new MetadataMappingMemoryService(this, MemoryService, SymbolService);
                        target.DisposeOnClose(SymbolService.OnChangeEvent.Register(_metadataMappingMemoryService.Flush));
                    }
                    return _metadataMappingMemoryService;
                });
            }
        }

        #region IRuntime

        public IServiceProvider Services => ServiceProvider;

        public int Id { get; }

        public RuntimeType RuntimeType { get; }

        public IModule RuntimeModule { get; }

        public string GetDacFilePath()
        {
            if (_dacFilePath == null)
            {
                string dacFileName = GetDacFileName();
                _dacFilePath = GetLocalDacPath(dacFileName);
                if (_dacFilePath == null)
                {
                    _dacFilePath = DownloadFile(dacFileName);
                }
            }
            return _dacFilePath;
        }

        public string GetDbiFilePath()
        {
            if (_dbiFilePath == null)
            {
                string dbiFileName = GetDbiFileName();
                _dbiFilePath = GetLocalPath(dbiFileName);
                if (_dbiFilePath == null)
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
            if (_clrRuntime == null)
            {
                string dacFilePath = GetDacFilePath();
                if (dacFilePath != null)
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
            Debug.Assert(!string.IsNullOrEmpty(_clrInfo.DacInfo.PlatformSpecificFileName));
            string name = _clrInfo.DacInfo.PlatformSpecificFileName;

            // If this is the Linux runtime module name, but we are running on Windows return the cross-OS DAC name.
            if (_target.OperatingSystem == OSPlatform.Linux && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                name = "mscordaccore.dll";
            }
            return name;
        }

        private string GetLocalDacPath(string dacFileName)
        {
            string dacFilePath;
            if (!string.IsNullOrEmpty(_runtimeService.RuntimeModuleDirectory))
            {
                dacFilePath = Path.Combine(_runtimeService.RuntimeModuleDirectory, dacFileName);
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
            string name = _target.GetPlatformModuleName("mscordbi");

            // If this is the Linux runtime module name, but we are running on Windows return the cross-OS DBI name.
            if (_target.OperatingSystem == OSPlatform.Linux && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                name = "mscordbi.dll";
            }
            return name;
        }

        private string GetLocalPath(string fileName)
        {
            string localFilePath;
            if (!string.IsNullOrEmpty(_runtimeService.RuntimeModuleDirectory))
            {
                localFilePath = Path.Combine(_runtimeService.RuntimeModuleDirectory, fileName);
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
            OSPlatform platform = _target.OperatingSystem;
            string filePath = null;

            if (SymbolService.IsSymbolStoreEnabled)
            {
                SymbolStoreKey key = null;

                if (platform == OSPlatform.OSX)
                {
                    KeyGenerator generator = MemoryService.GetKeyGenerator(
                        platform,
                        RuntimeModule.FileName,
                        RuntimeModule.ImageBase,
                        RuntimeModule.ImageSize);

                    key = generator.GetKeys(KeyTypeFlags.DacDbiKeys).SingleOrDefault((k) => Path.GetFileName(k.FullPathName) == fileName);
                }
                else if (platform == OSPlatform.Linux)
                {
                    if (!RuntimeModule.BuildId.IsDefaultOrEmpty)
                    {
                        IEnumerable<SymbolStoreKey> keys = ELFFileKeyGenerator.GetKeys(
                            KeyTypeFlags.DacDbiKeys,
                            RuntimeModule.FileName,
                            RuntimeModule.BuildId.ToArray(),
                            symbolFile: false,
                            symbolFileName: null);

                        key = keys.SingleOrDefault((k) => Path.GetFileName(k.FullPathName) == fileName);
                    }
                }
                else if (platform == OSPlatform.Windows)
                {
                    if (RuntimeModule.IndexTimeStamp.HasValue && RuntimeModule.IndexFileSize.HasValue)
                    {
                        // Use the coreclr.dll's id (timestamp/filesize) to download the the dac module.
                        key = PEFileKeyGenerator.GetKey(fileName, RuntimeModule.IndexTimeStamp.Value, RuntimeModule.IndexFileSize.Value);
                    }
                }

                if (key != null)
                {
                    // Now download the DAC module from the symbol server
                    filePath = SymbolService.DownloadFile(key);
                }
                else
                {
                    Trace.TraceInformation($"DownloadFile: {fileName}: key not generated");
                }
            }
            else
            {
                Trace.TraceInformation($"DownLoadFile: {fileName}: symbol store not enabled");
            }
            return filePath;
        }

        private IMemoryService MemoryService
        {
            get
            {
                if (_memoryService == null) {
                    _memoryService = _target.Services.GetService<IMemoryService>();
                }
                return _memoryService;
            }
        }

        private ISymbolService SymbolService
        {
            get
            {
                if (_symbolService == null) {
                    _symbolService = _target.Services.GetService<ISymbolService>();
                }
                return _symbolService;
            }
        }

        public override bool Equals(object obj)
        {
            return Id == ((Runtime)obj).Id;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
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
            sb.AppendLine($"    Runtime module path: {RuntimeModule.FileName}");
            if (_dacFilePath != null) {
                sb.AppendLine($"    DAC: {_dacFilePath}");
            }
            if (_dbiFilePath != null) {
                sb.AppendLine($"    DBI: {_dbiFilePath}");
            }
            return sb.ToString();
        }
    }
}
