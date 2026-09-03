// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime provider implementation
    /// </summary>
    [ProviderExport(Type = typeof(IRuntimeProvider))]
    public class RuntimeProvider : IRuntimeProvider
    {
        private const string RuntimeInfoExport = "DotNetRuntimeInfo";
        private const string ContractDescriptorExport = "DotNetRuntimeContractDescriptor";
        private const string RuntimeDebugHeaderExport = "DotNetRuntimeDebugHeader";

        private readonly IServiceProvider _services;

        public RuntimeProvider(IServiceProvider services)
        {
            _services = services;
        }

        #region IRuntimeProvider

        /// <summary>
        /// Returns the list of .NET runtimes in the target
        /// </summary>
        /// <param name="startingRuntimeId">The starting runtime id for this provider</param>
        /// <param name="flags">Enumeration control flags</param>
        public IEnumerable<IRuntime> EnumerateRuntimes(int startingRuntimeId, RuntimeEnumerationFlags flags)
        {
            // The ClrInfo and DataTarget instances are disposed when Runtime instance is disposed. Runtime instances are
            // not flushed when the Target/RuntimeService is flushed; they are all disposed and the list cleared. They are
            // all re-created the next time the IRuntime or ClrRuntime instance is queried.
            ISettingsService settingsService = _services.GetService<ISettingsService>();
            bool verifyDac = settingsService?.DacSignatureVerificationEnabled ?? true;
            IDataReader dataReader = _services.GetService<IDataReader>();
            IModule entryPoint = _services.GetService<IModuleService>()?.EntryPointModule;
            IReadOnlyList<ModuleInfo> runtimeModules = EnumerateRuntimeModules(
                dataReader,
                flags,
                entryPoint?.ImageBase,
                entryPoint?.FileName);

            // The cDAC (mscordaccore_universal) ships inside the (signed) diagnostics tool package and
            // carries no individual DAC signature, so it cannot satisfy ClrMD's signature check. Trust it
            // the same way the native and SOS-hosting cDAC load paths do (load without verification), while
            // still verifying the in-box DAC. We trust ONLY the exact cDAC path the host resolver provides
            // (the bundled binary next to sos); matching by file name alone would let a name-hijacked DLL
            // loaded from elsewhere (target runtime dir, symbol cache, ...) bypass verification.
            string trustedCDacPath = _services.GetService<IHostAssetResolver>()?.GetCDacPath();
            string normalizedTrustedCDacPath = string.IsNullOrEmpty(trustedCDacPath) ? null : Path.GetFullPath(trustedCDacPath);
            StringComparison pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            DataTargetOptions CreateDataTargetOptions(
                bool skipRuntimeEnumeration,
                bool forceCompleteRuntimeEnumeration) => new()
            {
                ForceCompleteRuntimeEnumeration = forceCompleteRuntimeEnumeration,
                SkipRuntimeEnumeration = skipRuntimeEnumeration,
                VerifyDacOnWindows = verifyDac,
                // Takes priority over VerifyDacOnWindows: skip verification only for the exact bundled cDAC.
                DacSignatureVerificationOverride = (dacFilePath) =>
                {
                    if (normalizedTrustedCDacPath is not null
                        && !string.IsNullOrEmpty(dacFilePath)
                        && string.Equals(Path.GetFullPath(dacFilePath), normalizedTrustedCDacPath, pathComparison))
                    {
                        return false;
                    }
                    return verifyDac;
                },
                SymbolProvider = _services.GetService<IClrSymbolProvider>(),
            };

            List<ClrInfo> runtimes;
            using (DataTarget discoveryTarget = new(
                new RuntimeModuleDataReader(dataReader, runtimeModules),
                CreateDataTargetOptions(
                    skipRuntimeEnumeration: false,
                    forceCompleteRuntimeEnumeration: true)))
            {
                runtimes = discoveryTarget.ClrVersions
                    .Select(clrInfo => CloneClrInfo(
                        clrInfo,
                        // ClrMD's legacy DAC loader currently requires DataTarget.ClrVersions to be
                        // populated. Keep enumeration enabled on the full-reader target until ClrMD
                        // supports registering host-discovered metadata without a loaded interface.
                        new DataTarget(
                            dataReader,
                            CreateDataTargetOptions(
                                skipRuntimeEnumeration: false,
                                forceCompleteRuntimeEnumeration:
                                    (flags & RuntimeEnumerationFlags.All) != 0))))
                    .ToList();
            }

            for (int i = 0; i < runtimes.Count; i++)
            {
                yield return new Runtime(_services, startingRuntimeId + i, runtimes[i]);
            }
        }

        internal static IReadOnlyList<ModuleInfo> EnumerateRuntimeModules(
            IDataReader dataReader,
            RuntimeEnumerationFlags flags,
            ulong? entryPointBase,
            string entryPointFileName)
        {
            if (dataReader is null)
            {
                throw new ArgumentNullException(nameof(dataReader));
            }

            List<ModuleInfo> modules = dataReader.EnumerateModules().ToList();
            if ((flags & RuntimeEnumerationFlags.All) != 0)
            {
                return modules;
            }

            HashSet<ulong> candidates = new();
            foreach (ModuleInfo module in modules)
            {
                if (IsNamedRuntimeModule(module.FileName))
                {
                    candidates.Add(module.ImageBase);
                }
            }

            if (entryPointBase.HasValue && !string.IsNullOrEmpty(entryPointFileName))
            {
                string entryPointName = GetFileNameWithoutExtension(entryPointFileName);
                foreach (ModuleInfo module in modules)
                {
                    bool isEntryPoint = module.ImageBase == entryPointBase.Value;
                    bool isEntryPointDll =
                        dataReader.TargetPlatform == OSPlatform.Windows &&
                        GetExtension(module.FileName).Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                        GetFileNameWithoutExtension(module.FileName).Equals(entryPointName, StringComparison.OrdinalIgnoreCase);
                    if ((isEntryPoint || isEntryPointDll) && HasRuntimeExport(module))
                    {
                        candidates.Add(module.ImageBase);
                    }
                }
            }

            return modules.Where(module => candidates.Contains(module.ImageBase)).ToList();
        }

        private static bool IsNamedRuntimeModule(string fileName)
        {
            string moduleName = GetFileName(fileName);
            return moduleName.Equals("clr.dll", StringComparison.OrdinalIgnoreCase)
                || moduleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase)
                || moduleName.Equals("libcoreclr.so", StringComparison.Ordinal)
                || moduleName.Equals("libcoreclr.dylib", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasRuntimeExport(ModuleInfo module)
        {
            try
            {
                return module.GetExportSymbolAddress(RuntimeInfoExport) != 0
                    || module.GetExportSymbolAddress(ContractDescriptorExport) != 0
                    || module.GetExportSymbolAddress(RuntimeDebugHeaderExport) != 0;
            }
            catch (Exception ex) when
                (ex is IOException or
                 InvalidDataException or
                 ArgumentException or
                 OverflowException or
                 DiagnosticsException)
            {
                return false;
            }
        }

        private static ClrInfo CloneClrInfo(ClrInfo source, DataTarget dataTarget) =>
            new(dataTarget, source.ModuleInfo, source.Version)
            {
                BuildId = source.BuildId,
                ContractDescriptorAddress = source.ContractDescriptorAddress,
                DebuggingLibraries = source.DebuggingLibraries,
                Flavor = source.Flavor,
                IndexFileSize = source.IndexFileSize,
                IndexTimeStamp = source.IndexTimeStamp,
                IsSingleFile = source.IsSingleFile,
            };

        private static string GetFileName(string path) =>
            Path.GetFileName(path.Replace('\\', '/'));

        private static string GetFileNameWithoutExtension(string path) =>
            Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));

        private static string GetExtension(string path) =>
            Path.GetExtension(path.Replace('\\', '/'));

        private sealed class RuntimeModuleDataReader : IDataReader
        {
            private readonly IDataReader _reader;
            private readonly IReadOnlyList<ModuleInfo> _modules;

            public RuntimeModuleDataReader(IDataReader reader, IReadOnlyList<ModuleInfo> modules)
            {
                _reader = reader ?? throw new ArgumentNullException(nameof(reader));
                _modules = modules ?? throw new ArgumentNullException(nameof(modules));
            }

            public string DisplayName => _reader.DisplayName;
            public bool IsThreadSafe => _reader.IsThreadSafe;
            public OSPlatform TargetPlatform => _reader.TargetPlatform;
            public Architecture Architecture => _reader.Architecture;
            public int ProcessId => _reader.ProcessId;
            public int PointerSize => _reader.PointerSize;
            public IEnumerable<ModuleInfo> EnumerateModules() => _modules;
            public bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context) =>
                _reader.GetThreadContext(threadID, contextFlags, context);
            public void FlushCachedData() => _reader.FlushCachedData();
            public int Read(ulong address, Span<byte> buffer) => _reader.Read(address, buffer);
            public bool Read<T>(ulong address, out T value) where T : unmanaged => _reader.Read(address, out value);
            public T Read<T>(ulong address) where T : unmanaged => _reader.Read<T>(address);
            public bool ReadPointer(ulong address, out ulong value) => _reader.ReadPointer(address, out value);
            public ulong ReadPointer(ulong address) => _reader.ReadPointer(address);
        }

        #endregion
    }
}
