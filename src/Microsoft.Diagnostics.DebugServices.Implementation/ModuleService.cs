// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
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
        const uint VmProtWrite = 0x02;

        internal protected readonly ITarget Target;
        internal protected IMemoryService RawMemoryService;
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
                        if (module is IDisposable disposable) {
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
            if (!GetModules().TryGetValue(baseAddress, out IModule module)) {
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

                if (address >= start && address < end) {
                    return module;
                }

                if (module.ImageBase < address) {
                    min = mid + 1;
                }
                else { 
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
        /// <param name="pdbFileInfos">the pdb records or null</param>
        /// <param name="moduleFlags">module flags</param>
        /// <returns>PEImage instance or null</returns>
        internal PEFile GetPEInfo(ulong address, ulong size, out IEnumerable<PdbFileInfo> pdbFileInfos, ref Module.Flags moduleFlags)
        {
            PEFile peFile = null;

            // Start off with no pdb infos and as a native non-PE non-managed module
            pdbFileInfos = Array.Empty<PdbFileInfo>();
            moduleFlags &= ~(Module.Flags.IsPEImage | Module.Flags.IsManaged | Module.Flags.IsLoadedLayout | Module.Flags.IsFileLayout);

            // None of the modules that lldb (on either Linux/MacOS) provides are PEs
            if (size > 0 && Target.Host.HostType != HostType.Lldb)
            {
                // First try getting the PE info as loaded layout (native Windows DLLs and most managed PEs).
                peFile = GetPEInfo(isVirtual: true, address, size, out List<PdbFileInfo> pdbs, out Module.Flags flags);

                // Continue only if marked as a PE. This bit regardless of the layout if the module has a PE header/signature.
                if ((flags & Module.Flags.IsPEImage) != 0)
                {
                    if (peFile is null || pdbs.Count == 0)
                    {
                        // If PE file is invalid or there are no PDB records, try getting the PE info as file layout. No PDB records can mean
                        // that either the layout is wrong or that there really no PDB records. If file layout doesn't have any pdb records
                        // either default to loaded layout PEFile.
                        PEFile peFileLayout = GetPEInfo(isVirtual: false, address, size, out List<PdbFileInfo> pdbsFileLayout, out Module.Flags flagsFileLayout);
                        Debug.Assert((flagsFileLayout & Module.Flags.IsPEImage) != 0);
                        if (peFileLayout is not null && (peFile is null || pdbsFileLayout.Count > 0))
                        {
                            flags = flagsFileLayout;
                            pdbs = pdbsFileLayout;
                            peFile = peFileLayout;
                        }
                    }
                    if (peFile is not null)
                    {
                        moduleFlags |= flags;
                        pdbFileInfos = pdbs;
                    }
                }
            }

            return peFile;
        }

        /// <summary>
        /// Returns information about the PE file for a specific layout.
        /// </summary>
        /// <param name="isVirtual">the memory layout of the module</param>
        /// <param name="address">module base address</param>
        /// <param name="size">module size</param>
        /// <param name="pdbs">pdb infos</param>
        /// <param name="flags">module flags</param>
        /// <returns>PEFile instance or null</returns>
        private PEFile GetPEInfo(bool isVirtual, ulong address, ulong size, out List<PdbFileInfo> pdbs, out Module.Flags flags)
        {
            pdbs = null;
            flags = 0;
            try
            {
                Stream stream = RawMemoryService.CreateMemoryStream(address, size);
                PEFile peFile = new(new StreamAddressSpace(stream), isVirtual);
                if (peFile.IsValid())
                {
                    flags |= Module.Flags.IsPEImage;
                    flags |= peFile.IsILImage ? Module.Flags.IsManaged : Module.Flags.None;
                    pdbs = peFile.Pdbs.Select((pdb) => pdb.ToPdbFileInfo()).ToList();
                    flags |= isVirtual ? Module.Flags.IsLoadedLayout : Module.Flags.IsFileLayout;
                    return peFile;
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException)
            {
                Trace.TraceError($"GetPEInfo: {address:X16} isVirtual {isVirtual} exception {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Returns the ELF module build id or the MachO module uuid
        /// </summary>
        /// <param name="address">module base address</param>
        /// <returns>build id or null</returns>
        internal byte[] GetBuildId(ulong address)
        {
            // This code is called by the image mapping memory service so it needs to use the
            // original or raw memory service to prevent recursion so it can't use the ELFFile
            // or MachOFile instance that is available from the IModule.Services provider.
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
        /// <param name="module">module to get version string</param>
        /// <returns>version string or null</returns>
        protected string GetVersionString(IModule module)
        {
            try
            {
                ELFFile elfFile = module.Services.GetService<ELFFile>();
                if (elfFile is not null)
                {
                    foreach (ELFProgramHeader programHeader in elfFile.Segments.Select((segment) => segment.Header))
                    {
                        uint flags = MemoryService.PointerSize == 8 ? programHeader.Flags : programHeader.Flags32;
                        if (programHeader.Type == ELFProgramHeaderType.Load &&
                           (flags & (uint)ELFProgramHeaderAttributes.Writable) != 0)
                        {
                            ulong loadAddress = programHeader.VirtualAddress.Value;
                            long loadSize = (long)programHeader.VirtualSize;
                            if (SearchVersionString(module.ImageBase + loadAddress, loadSize, out string productVersion))
                            {
                                return productVersion;
                            }
                        }
                    }
                    Trace.TraceInformation($"GetVersionString: not found in ELF file {module}");
                }
                else
                {
                    MachOFile machOFile = module.Services.GetService<MachOFile>();
                    if (machOFile is not null)
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
                        Trace.TraceInformation($"GetVersionString: not found in MachO file {module}");
                    }
                    else
                    {
                        Trace.TraceError($"GetVersionString: unsupported module {module} on platform {Target.OperatingSystem}");
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException || ex is IOException)
            {
                Trace.TraceError($"GetVersionString: {module} exception {ex.Message}");
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
                _versionCache = new ReadVirtualCache(MemoryService);
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
            if (Target.OperatingSystem == OSPlatform.Windows) {
                return StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(module.FileName), moduleName);
            }
            else {
                return string.Equals(Path.GetFileName(module.FileName), moduleName);
            }
        } 

        internal protected IMemoryService MemoryService => _memoryService ??= Target.Services.GetService<IMemoryService>();

        internal protected ISymbolService SymbolService => _symbolService ??= Target.Services.GetService<ISymbolService>(); 

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
