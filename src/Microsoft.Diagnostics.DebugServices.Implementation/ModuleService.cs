// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Module service base implementation
    /// </summary>
    public abstract class ModuleService : IModuleService
    {
        [Flags]
        internal enum ELFProgramHeaderAttributes : uint
        {
            Executable = 1,             // PF_X
            Writable = 2,               // PF_W
            Readable = 4,               // PF_R
            OSMask = 0x0FF00000,        // PF_MASKOS
            ProcessorMask = 0xF0000000, // PF_MASKPROC
        }

        // MachO writable segment attribute
        private const uint VmProtWrite = 0x02;

        protected internal readonly ITarget Target;
        protected internal IMemoryService RawMemoryService;
        private IMemoryService _memoryService;
        private ISymbolService _symbolService;
        private ReadVirtualCache _versionCache;
        private Dictionary<ulong, IModule> _modules;
        private IModule[] _sortedByBaseAddress;

        private static readonly byte[] s_versionString = Encoding.ASCII.GetBytes("@(#)Version ");
        private static readonly int s_versionLength = s_versionString.Length;

        public ModuleService(ITarget target, IMemoryService rawMemoryService)
        {
            Debug.Assert(target != null);
            Target = target;
            RawMemoryService = rawMemoryService;

            target.OnFlushEvent.Register(() => {
                _versionCache?.Clear();
                if (_modules != null)
                {
                    foreach (IModule module in _modules.Values)
                    {
                        if (module is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
                _modules = null;
                _sortedByBaseAddress = null;
            });
        }

        #region IModuleService

        /// <summary>
        /// Enumerate all the modules in the target
        /// </summary>
        IEnumerable<IModule> IModuleService.EnumerateModules()
        {
            return GetSortedModules();
        }

        /// <summary>
        /// Get the module info from the module index
        /// </summary>
        /// <param name="moduleIndex">index</param>
        /// <returns>module</returns>
        /// <exception cref="DiagnosticsException">invalid module index</exception>
        IModule IModuleService.GetModuleFromIndex(int moduleIndex)
        {
            try
            {
                return GetModules().First((pair) => pair.Value.ModuleIndex == moduleIndex).Value;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new DiagnosticsException($"Invalid module index: {moduleIndex}", ex);
            }
        }

        /// <summary>
        /// Get the module info from the module base address
        /// </summary>
        /// <param name="baseAddress"></param>
        /// <returns>module</returns>
        /// <exception cref="DiagnosticsException">base address not found</exception>
        IModule IModuleService.GetModuleFromBaseAddress(ulong baseAddress)
        {
            if (!GetModules().TryGetValue(baseAddress, out IModule module))
            {
                throw new DiagnosticsException($"Invalid module base address: {baseAddress:X16}");
            }
            return module;
        }

        /// <summary>
        /// Finds the module that contains the address.
        /// </summary>
        /// <param name="address">search address</param>
        /// <returns>module or null</returns>
        IModule IModuleService.GetModuleFromAddress(ulong address)
        {
            Debug.Assert((address & ~RawMemoryService.SignExtensionMask()) == 0);
            IModule[] modules = GetSortedModules();
            int min = 0, max = modules.Length - 1;

            // Check if there is a module that contains the address range
            while (min <= max)
            {
                int mid = (min + max) / 2;
                IModule module = modules[mid];

                ulong start = module.ImageBase;
                Debug.Assert((start & ~RawMemoryService.SignExtensionMask()) == 0);
                ulong end = start + module.ImageSize;

                if (address >= start && address < end)
                {
                    return module;
                }

                if (module.ImageBase < address)
                {
                    min = mid + 1;
                }
                else
                {
                    max = mid - 1;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the module(s) with the specified module name. It is the platform dependent
        /// name that includes the "lib" prefix on xplat and the extension (dll, so or dylib).
        /// </summary>
        /// <param name="moduleName">module name to find</param>
        /// <returns>matching modules</returns>
        IEnumerable<IModule> IModuleService.GetModuleFromModuleName(string moduleName)
        {
            moduleName = Path.GetFileName(moduleName);
            foreach (IModule module in GetModules().Values)
            {
                if (IsModuleEqual(module, moduleName))
                {
                    yield return module;
                }
            }
        }

        #endregion

        /// <summary>
        /// Get/create the modules dictionary.
        /// </summary>
        private Dictionary<ulong, IModule> GetModules()
        {
            if (_modules == null)
            {
                _modules = GetModulesInner();
            }
            return _modules;
        }

        /// <summary>
        /// Create the sorted array of modules.
        /// </summary>
        /// <returns></returns>
        private IModule[] GetSortedModules()
        {
            if (_sortedByBaseAddress == null)
            {
                _sortedByBaseAddress = GetModules().OrderBy((pair) => pair.Key).Select((pair) => pair.Value).ToArray();
            }
            return _sortedByBaseAddress;
        }

        /// <summary>
        /// Get/create the modules.
        /// </summary>
        protected abstract Dictionary<ulong, IModule> GetModulesInner();

        /// <summary>
        /// Returns the PE file's PDB info from the debug directory
        /// </summary>
        /// <param name="address">module base address</param>
        /// <param name="size">module size</param>
        /// <param name="pdbFileInfo">the pdb record or null</param>
        /// <param name="flags">module flags</param>
        /// <returns>PEImage instance or null</returns>
        internal PEImage GetPEInfo(ulong address, ulong size, ref PdbFileInfo pdbFileInfo, ref Module.Flags flags)
        {
            PEImage peImage = null;

            // None of the modules that lldb (on either Linux/MacOS) provides are PEs
            if (Target.Host.HostType != HostType.Lldb)
            {
                // First try getting the PE info as load layout (native Windows DLLs and most managed PEs on Linux/MacOS).
                peImage = GetPEInfo(isVirtual: true, address: address, size: size, pdbFileInfo: ref pdbFileInfo, flags: ref flags);
                if (peImage == null)
                {
                    if (Target.OperatingSystem != OSPlatform.Windows)
                    {
                        // Then try getting the PE info as file layout (some managed PEs on Linux/MacOS).
                        peImage = GetPEInfo(isVirtual: false, address: address, size: size, pdbFileInfo: ref pdbFileInfo, flags: ref flags);
                    }
                }
            }
            return peImage;
        }

        /// <summary>
        /// Returns information about the PE file.
        /// </summary>
        /// <param name="isVirtual">the memory layout of the module</param>
        /// <param name="address">module base address</param>
        /// <param name="size">module size</param>
        /// <param name="pdbFileInfo">the pdb record or null</param>
        /// <param name="flags">module flags</param>
        ///
        /// <returns>PEImage instance or null</returns>
        private PEImage GetPEInfo(bool isVirtual, ulong address, ulong size, ref PdbFileInfo pdbFileInfo, ref Module.Flags flags)
        {
            Stream stream = RawMemoryService.CreateMemoryStream(address, size);
            try
            {
                stream.Position = 0;
                var peImage = new PEImage(stream, leaveOpen: false, isVirtual);
                if (peImage.IsValid)
                {
                    flags |= Module.Flags.IsPEImage;
                    flags |= peImage.IsManaged ? Module.Flags.IsManaged : Module.Flags.None;
                    pdbFileInfo = peImage.DefaultPdb?.ToPdbFileInfo();
                    flags &= ~(Module.Flags.IsLoadedLayout | Module.Flags.IsFileLayout);
                    flags |= isVirtual ? Module.Flags.IsLoadedLayout : Module.Flags.IsFileLayout;
                    return peImage;
                }
                else
                {
                    Trace.TraceError($"GetPEInfo: PE invalid {address:X16} isVirtual {isVirtual}");
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException || ex is EndOfStreamException || ex is IOException)
            {
                Trace.TraceError($"GetPEInfo: {address:X16} isVirtual {isVirtual} exception {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Finds or downloads the module and creates a PEReader for it.
        /// </summary>
        /// <param name="module">module instance</param>
        /// <returns>reader or null</returns>
        internal PEReader GetPEReader(IModule module)
        {
            if (!module.IndexTimeStamp.HasValue || !module.IndexFileSize.HasValue)
            {
                Trace.TraceWarning($"GetPEReader: module {module.FileName} has no index timestamp/filesize");
                return null;
            }

            SymbolStoreKey moduleKey = PEFileKeyGenerator.GetKey(Path.GetFileName(module.FileName), module.IndexTimeStamp.Value, module.IndexFileSize.Value);
            if (moduleKey is null)
            {
                Trace.TraceWarning($"GetPEReader: no index generated for module {module.FileName} ");
                return null;
            }

            if (File.Exists(module.FileName))
            {
                Stream stream = OpenFile(module.FileName);
                if (stream is not null)
                {
                    var peFile = new PEFile(new StreamAddressSpace(stream), false);
                    var generator = new PEFileKeyGenerator(Tracer.Instance, peFile, module.FileName);
                    IEnumerable<SymbolStoreKey> keys = generator.GetKeys(KeyTypeFlags.IdentityKey);
                    foreach (SymbolStoreKey key in keys)
                    {
                        if (moduleKey.Equals(key))
                        {
                            Trace.TraceInformation("GetPEReader: local file match {0}", module.FileName);
                            return OpenPEReader(module.FileName);
                        }
                    }
                }
            }

            // Now download the module from the symbol server if local file doesn't exists or doesn't have the right key
            string downloadFilePath = SymbolService.DownloadFile(moduleKey);
            if (!string.IsNullOrEmpty(downloadFilePath))
            {
                Trace.TraceInformation("GetPEReader: downloaded {0}", downloadFilePath);
                return OpenPEReader(downloadFilePath);
            }

            return null;
        }

        /// <summary>
        /// Opens and returns an PEReader instance from the local file path
        /// </summary>
        /// <param name="filePath">PE file to open</param>
        /// <returns>PEReader instance or null</returns>
        private PEReader OpenPEReader(string filePath)
        {
            Stream stream = OpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    var reader = new PEReader(stream);
                    if (reader.PEHeaders == null || reader.PEHeaders.PEHeader == null)
                    {
                        Trace.TraceError($"OpenPEReader: PEReader invalid headers");
                        return null;
                    }
                    return reader;
                }
                catch (Exception ex) when (ex is BadImageFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenPEReader: PEReader exception {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Finds or downloads the ELF module and creates a ELFFile instance for it.
        /// </summary>
        /// <param name="module">module instance</param>
        /// <returns>ELFFile instance or null</returns>
        internal ELFFile GetELFFile(IModule module)
        {
            if (module.BuildId.IsDefaultOrEmpty)
            {
                Trace.TraceWarning($"GetELFFile: module {module.FileName} has no build id");
                return null;
            }

            SymbolStoreKey moduleKey = ELFFileKeyGenerator.GetKeys(KeyTypeFlags.IdentityKey, module.FileName, module.BuildId.ToArray(), symbolFile: false, symbolFileName: null).SingleOrDefault();
            if (moduleKey is null)
            {
                Trace.TraceWarning($"GetELFFile: no index generated for module {module.FileName} ");
                return null;
            }

            if (File.Exists(module.FileName))
            {
                ELFFile elfFile = OpenELFFile(module.FileName);
                if (elfFile is not null)
                {
                    var generator = new ELFFileKeyGenerator(Tracer.Instance, elfFile, module.FileName);
                    IEnumerable<SymbolStoreKey> keys = generator.GetKeys(KeyTypeFlags.IdentityKey);
                    foreach (SymbolStoreKey key in keys)
                    {
                        if (moduleKey.Equals(key))
                        {
                            Trace.TraceInformation("GetELFFile: local file match {0}", module.FileName);
                            return elfFile;
                        }
                    }
                }
            }

            // Now download the module from the symbol server if local file doesn't exists or doesn't have the right key
            string downloadFilePath = SymbolService.DownloadFile(moduleKey);
            if (!string.IsNullOrEmpty(downloadFilePath))
            {
                Trace.TraceInformation("GetELFFile: downloaded {0}", downloadFilePath);
                return OpenELFFile(downloadFilePath);
            }

            return null;
        }

        /// <summary>
        /// Opens and returns an ELFFile instance from the local file path
        /// </summary>
        /// <param name="filePath">ELF file to open</param>
        /// <returns>ELFFile instance or null</returns>
        private ELFFile OpenELFFile(string filePath)
        {
            Stream stream = OpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    ELFFile elfFile = new ELFFile(new StreamAddressSpace(stream), position: 0, isDataSourceVirtualAddressSpace: false);
                    if (!elfFile.IsValid())
                    {
                        Trace.TraceError($"OpenELFFile: not a valid file");
                        return null;
                    }
                    return elfFile;
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenELFFile: exception {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Finds or downloads the ELF module and creates a MachOFile instance for it.
        /// </summary>
        /// <param name="module">module instance</param>
        /// <returns>MachO file instance or null</returns>
        internal MachOFile GetMachOFile(IModule module)
        {
            if (module.BuildId.IsDefaultOrEmpty)
            {
                Trace.TraceWarning($"GetMachOFile: module {module.FileName} has no build id");
                return null;
            }

            SymbolStoreKey moduleKey = MachOFileKeyGenerator.GetKeys(KeyTypeFlags.IdentityKey, module.FileName, module.BuildId.ToArray(), symbolFile: false, symbolFileName: null).SingleOrDefault();
            if (moduleKey is null)
            {
                Trace.TraceWarning($"GetMachOFile: no index generated for module {module.FileName} ");
                return null;
            }

            if (File.Exists(module.FileName))
            {
                MachOFile machOFile = OpenMachOFile(module.FileName);
                if (machOFile is not null)
                {
                    var generator = new MachOFileKeyGenerator(Tracer.Instance, machOFile, module.FileName);
                    IEnumerable<SymbolStoreKey> keys = generator.GetKeys(KeyTypeFlags.IdentityKey);
                    foreach (SymbolStoreKey key in keys)
                    {
                        if (moduleKey.Equals(key))
                        {
                            Trace.TraceInformation("GetMachOFile: local file match {0}", module.FileName);
                            return machOFile;
                        }
                    }
                }
            }

            // Now download the module from the symbol server if local file doesn't exists or doesn't have the right key
            string downloadFilePath = SymbolService.DownloadFile(moduleKey);
            if (!string.IsNullOrEmpty(downloadFilePath))
            {
                Trace.TraceInformation("GetMachOFile: downloaded {0}", downloadFilePath);
                return OpenMachOFile(downloadFilePath);
            }

            return null;
        }

        /// <summary>
        /// Opens and returns an MachOFile instance from the local file path
        /// </summary>
        /// <param name="filePath">MachO file to open</param>
        /// <returns>MachOFile instance or null</returns>
        private MachOFile OpenMachOFile(string filePath)
        {
            Stream stream = OpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    var machoFile = new MachOFile(new StreamAddressSpace(stream), position: 0, dataSourceIsVirtualAddressSpace: false);
                    if (!machoFile.IsValid())
                    {
                        Trace.TraceError($"OpenMachOFile: not a valid file");
                        return null;
                    }
                    return machoFile;
                }
                catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenMachOFile: exception {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Opens and returns a file stream
        /// </summary>
        /// <param name="filePath">file to open</param>
        /// <returns>stream or null</returns>
        private Stream OpenFile(string filePath)
        {
            try
            {
                return File.OpenRead(filePath);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException || ex is FileNotFoundException || ex is UnauthorizedAccessException || ex is IOException)
            {
                Trace.TraceError($"OpenFile: OpenRead exception {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns the ELF module build id or the MachO module uuid
        /// </summary>
        /// <param name="address">module base address</param>
        /// <returns>build id or null</returns>
        internal byte[] GetBuildId(ulong address)
        {
            Stream stream = RawMemoryService.CreateMemoryStream();
            byte[] buildId = null;
            try
            {
                if (Target.OperatingSystem == OSPlatform.Linux)
                {
                    var elfFile = new ELFFile(new StreamAddressSpace(stream), address, true);
                    if (elfFile.IsValid())
                    {
                        buildId = elfFile.BuildID;
                    }
                }
                else if (Target.OperatingSystem == OSPlatform.OSX)
                {
                    var machOFile = new MachOFile(new StreamAddressSpace(stream), address, true);
                    if (machOFile.IsValid())
                    {
                        buildId = machOFile.Uuid;
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
            {
                Trace.TraceError($"GetBuildId: {address:X16} exception {ex.Message}");
            }
            return buildId;
        }

        /// <summary>
        /// Get the version string from a Linux or MacOS image
        /// </summary>
        /// <param name="address">image base</param>
        /// <returns>version string or null</returns>
        protected string GetVersionString(ulong address)
        {
            Stream stream = RawMemoryService.CreateMemoryStream();
            try
            {
                if (Target.OperatingSystem == OSPlatform.Linux)
                {
                    var elfFile = new ELFFile(new StreamAddressSpace(stream), address, true);
                    if (elfFile.IsValid())
                    {
                        foreach (ELFProgramHeader programHeader in elfFile.Segments.Select((segment) => segment.Header))
                        {
                            uint flags = RawMemoryService.PointerSize == 8 ? programHeader.Flags : programHeader.Flags32;
                            if (programHeader.Type == ELFProgramHeaderType.Load &&
                               (flags & (uint)ELFProgramHeaderAttributes.Writable) != 0)
                            {
                                ulong loadAddress = programHeader.VirtualAddress.Value;
                                long loadSize = (long)programHeader.VirtualSize;
                                if (SearchVersionString(address + loadAddress, loadSize, out string productVersion))
                                {
                                    return productVersion;
                                }
                            }
                        }
                    }
                }
                else if (Target.OperatingSystem == OSPlatform.OSX)
                {
                    var machOFile = new MachOFile(new StreamAddressSpace(stream), address, true);
                    if (machOFile.IsValid())
                    {
                        foreach (MachSegmentLoadCommand loadCommand in machOFile.Segments.Select((segment) => segment.LoadCommand))
                        {
                            if (loadCommand.Command == LoadCommandType.Segment64 &&
                               (loadCommand.InitProt & VmProtWrite) != 0 &&
                                loadCommand.SegName.ToString() != "__LINKEDIT")
                            {
                                ulong loadAddress = loadCommand.VMAddress + machOFile.PreferredVMBaseAddress;
                                long loadSize = (long)loadCommand.VMSize;
                                if (SearchVersionString(loadAddress, loadSize, out string productVersion))
                                {
                                    return productVersion;
                                }
                            }
                        }
                    }
                }
                else
                {
                    Trace.TraceError("GetVersionString: unsupported platform {0}", Target.OperatingSystem);
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
            {
                Trace.TraceError($"GetVersionString: {address:X16} exception {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Linux/MacOS version string search helper
        /// </summary>
        /// <param name="address">beginning of module memory</param>
        /// <param name="size">size of module</param>
        /// <param name="fileVersion">returned version string</param>
        /// <returns>true if successful</returns>
        private bool SearchVersionString(ulong address, long size, out string fileVersion)
        {
            byte[] buffer = new byte[s_versionString.Length];

            if (_versionCache == null)
            {
                // We use the possibly mapped memory service to find the version string in case it isn't in the dump.
                _versionCache = new ReadVirtualCache(Target.Services.GetService<IMemoryService>());
            }
            _versionCache.Clear();

            while (size > 0)
            {
                bool result = _versionCache.Read(address, buffer, s_versionString.Length, out int cbBytesRead);
                if (result && cbBytesRead >= s_versionLength)
                {
                    if (s_versionString.SequenceEqual(buffer))
                    {
                        address += (ulong)s_versionLength;
                        size -= s_versionLength;

                        var sb = new StringBuilder();
                        byte[] ch = new byte[1];
                        while (true)
                        {
                            // Now read the version string a char/byte at a time
                            result = _versionCache.Read(address, ch, ch.Length, out cbBytesRead);

                            // Return not found if there are any failures or problems while reading the version string.
                            if (!result || cbBytesRead < ch.Length || size <= 0)
                            {
                                break;
                            }

                            // Found the end of the string
                            if (ch[0] == '\0')
                            {
                                fileVersion = sb.ToString();
                                return true;
                            }
                            sb.Append(Encoding.ASCII.GetChars(ch));
                            address++;
                            size--;
                        }
                        // Return not found if overflowed the fileVersionBuffer (not finding a null).
                        break;
                    }
                    address++;
                    size--;
                }
                else
                {
                    address += (ulong)s_versionLength;
                    size -= s_versionLength;
                }
            }

            fileVersion = null;
            return false;
        }

        private bool IsModuleEqual(IModule module, string moduleName)
        {
            if (Target.OperatingSystem == OSPlatform.Windows)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(module.FileName), moduleName);
            }
            else
            {
                return string.Equals(Path.GetFileName(module.FileName), moduleName);
            }
        }

        protected internal IMemoryService MemoryService => _memoryService ??= Target.Services.GetService<IMemoryService>();

        protected internal ISymbolService SymbolService => _symbolService ??= Target.Services.GetService<ISymbolService>();

        /// <summary>
        /// Search memory helper class
        /// </summary>
        internal class ReadVirtualCache
        {
            private const int CACHE_SIZE = 4096;

            private readonly IMemoryService _memoryService;
            private readonly byte[] _cache = new byte[CACHE_SIZE];
            private ulong _startCache;
            private bool _cacheValid;
            private int _cacheSize;

            internal ReadVirtualCache(IMemoryService memoryService)
            {
                _memoryService = memoryService;
                Clear();
            }

            internal bool Read(ulong address, byte[] buffer, int bufferSize, out int bytesRead)
            {
                bytesRead = 0;

                if (bufferSize == 0)
                {
                    return true;
                }

                if (bufferSize > CACHE_SIZE)
                {
                    // Don't even try with the cache
                    return _memoryService.ReadMemory(address, buffer, bufferSize, out bytesRead);
                }

                if (!_cacheValid || (address < _startCache) || (address > (_startCache + (ulong)(_cacheSize - bufferSize))))
                {
                    _cacheValid = false;
                    _startCache = address;
                    if (!_memoryService.ReadMemory(_startCache, _cache, _cache.Length, out int cbBytesRead))
                    {
                        return false;
                    }
                    _cacheSize = cbBytesRead;
                    _cacheValid = true;
                }

                int size = Math.Min(bufferSize, _cacheSize);
                Array.Copy(_cache, (int)(address - _startCache), buffer, 0, size);
                bytesRead = size;
                return true;
            }

            internal void Clear()
            {
                _cacheValid = false;
                _cacheSize = CACHE_SIZE;
            }
        }
    }
}
