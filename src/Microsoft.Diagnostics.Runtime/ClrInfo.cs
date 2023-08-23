// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Interfaces;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents information about a single CLR in a process.
    /// </summary>
    public sealed class ClrInfo : IClrInfo
    {
        private const string c_desktopModuleName = "clr.dll";
        private const string c_coreModuleName = "coreclr.dll";
        private const string c_linuxCoreModuleName = "libcoreclr.so";
        private const string c_macOSCoreModuleName = "libcoreclr.dylib";

        private const string c_desktopDacFileNameBase = "mscordacwks";
        private const string c_coreDacFileNameBase = "mscordaccore";
        private const string c_desktopDacFileName = c_desktopDacFileNameBase + ".dll";
        private const string c_coreDacFileName = c_coreDacFileNameBase + ".dll";
        private const string c_linuxCoreDacFileName = "libmscordaccore.so";
        private const string c_macOSCoreDacFileName = "libmscordaccore.dylib";

        private const string c_windowsDbiFileName = "mscordbi.dll";
        private const string c_linuxCoreDbiFileName = "libmscordbi.so";
        private const string c_macOSCoreDbiFileName = "libmscordbi.dylib";


        internal ClrInfo(DataTarget dt, ClrFlavor flavor, ModuleInfo module, ulong runtimeInfo)
        {
            DataTarget = dt ?? throw new ArgumentNullException(nameof(dt));
            Flavor = flavor;
            ModuleInfo = module ?? throw new ArgumentNullException(nameof(module));
            IsSingleFile = runtimeInfo != 0;

            List<DebugLibraryInfo> artifacts = new(8);

            OSPlatform currentPlatform = GetCurrentPlatform();
            OSPlatform targetPlatform = dt.DataReader.TargetPlatform;
            Architecture currentArch = RuntimeInformation.ProcessArchitecture;
            Architecture targetArch = dt.DataReader.Architecture;

            string? dacCurrentPlatform = GetDacFileName(flavor, currentPlatform);
            string? dacTargetPlatform = GetDacFileName(flavor, targetPlatform);
            string? dbiCurrentPlatform = GetDbiFileName(flavor, currentPlatform);
            string? dbiTargetPlatform = GetDbiFileName(flavor, targetPlatform);
            if (IsSingleFile)
            {
                if (ClrRuntimeInfo.TryReadClrRuntimeInfo(DataTarget.DataReader, runtimeInfo, out ClrRuntimeInfo info, out Version version))
                {
                    if (dt.DataReader.TargetPlatform == OSPlatform.Windows)
                    {
                        IndexTimeStamp = info.RuntimePEProperties.TimeStamp;
                        IndexFileSize = info.RuntimePEProperties.FileSize;

                        if (dacTargetPlatform is not null)
                        {
                            (int timeStamp, int fileSize) = info.DacPEProperties;
                            if (timeStamp != 0 && fileSize != 0)
                                artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dac, dacTargetPlatform, targetArch, SymbolProperties.Self, fileSize, timeStamp));
                        }

                        if (dbiTargetPlatform is not null)
                        {
                            (int timeStamp, int fileSize) = info.DbiPEProperties;
                            if (timeStamp != 0 && fileSize != 0)
                                artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dbi, dbiTargetPlatform, targetArch, SymbolProperties.Self, fileSize, timeStamp));
                        }
                    }
                    else
                    {
                        BuildId = info.RuntimeBuildId;

                        if (dacTargetPlatform is not null)
                        {
                            ImmutableArray<byte> dacBuild = info.DacBuildId;
                            if (!dacBuild.IsDefaultOrEmpty)
                                artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dac, dacTargetPlatform, targetArch, targetPlatform, SymbolProperties.Self, dacBuild));
                        }

                        if (dbiTargetPlatform is not null)
                        {
                            ImmutableArray<byte> dbiBuild = info.DbiBuildId;
                            if (!dbiBuild.IsDefaultOrEmpty)
                                artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dbi, dbiTargetPlatform, targetArch, targetPlatform, SymbolProperties.Self, dbiBuild));
                        }
                    }
                }
                Version = version;
            }
            else
            {
                IndexTimeStamp = module.IndexTimeStamp;
                IndexFileSize = module.IndexFileSize;
                BuildId = module.BuildId;
                Version = module.Version;
            }

            // Long-name dac
            if (dt.DataReader.TargetPlatform == OSPlatform.Windows && Version.Major != 0)
                artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dac, GetWindowsLongNameDac(flavor, currentArch, targetArch, Version), currentArch, SymbolProperties.Coreclr, IndexFileSize, IndexTimeStamp));

            // Short-name dac under CLR's properties
            if (targetPlatform == currentPlatform)
            {
                // We are debugging the process on the same operating system.
                if (dacCurrentPlatform is not null)
                {
                    bool foundLocalDac = false;

                    // Check if the user has the same CLR installed locally, and if so
                    string? directory = Path.GetDirectoryName(module.FileName);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        string potentialClr = Path.Combine(directory, Path.GetFileName(module.FileName));
                        if (File.Exists(potentialClr))
                        {
                            try
                            {
                                using PEImage peimage = new(File.OpenRead(potentialClr));
                                if (peimage.IndexFileSize == IndexFileSize && peimage.IndexTimeStamp == IndexTimeStamp)
                                {
                                    string dacFound = Path.Combine(directory, dacCurrentPlatform);
                                    if (File.Exists(dacFound))
                                    {
                                        dacCurrentPlatform = dacFound;
                                        foundLocalDac = true;
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                    }

                    if (IndexFileSize != 0 && IndexTimeStamp != 0)
                    {
                        DebugLibraryInfo dacLibraryInfo = new(DebugLibraryKind.Dac, dacCurrentPlatform, targetArch, SymbolProperties.Coreclr, IndexFileSize, IndexTimeStamp);
                        if (foundLocalDac)
                            artifacts.Insert(0, dacLibraryInfo);
                        else
                            artifacts.Add(dacLibraryInfo);
                    }

                    if (!BuildId.IsDefaultOrEmpty)
                    {
                        DebugLibraryInfo dacLibraryInfo = new(DebugLibraryKind.Dac, dacCurrentPlatform, targetArch, targetPlatform, SymbolProperties.Coreclr, BuildId);
                        if (foundLocalDac)
                            artifacts.Insert(0, dacLibraryInfo);
                        else
                            artifacts.Add(dacLibraryInfo);
                    }
                }

                if (dbiCurrentPlatform is not null)
                {
                    if (IndexFileSize != 0 && IndexTimeStamp != 0)
                    {
                        artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dbi, dbiCurrentPlatform, targetArch, SymbolProperties.Coreclr, IndexFileSize, IndexTimeStamp));
                    }

                    if (!BuildId.IsDefaultOrEmpty)
                    {
                        artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dbi, dbiCurrentPlatform, targetArch, targetPlatform, SymbolProperties.Coreclr, BuildId));
                    }
                }
            }
            else
            {
                // We are debugging the process on a different operating system.
                if (IndexFileSize != 0 && IndexTimeStamp != 0)
                {
                    // We currently only support cross-os debugging on windows targeting linux or os x runtimes.  So if we have windows properties,
                    // then we only generate one artifact (the target one).
                    if (dacTargetPlatform is not null)
                        artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dac, dacTargetPlatform, targetArch, SymbolProperties.Coreclr, IndexFileSize, IndexTimeStamp));

                    if (dbiTargetPlatform is not null)
                        artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dbi, dbiTargetPlatform, targetArch, SymbolProperties.Coreclr, IndexFileSize, IndexTimeStamp));
                }

                if (!BuildId.IsDefaultOrEmpty)
                {
                    if (dacTargetPlatform is not null)
                        artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dac, dacTargetPlatform, targetArch, targetPlatform, SymbolProperties.Coreclr, BuildId));

                    if (dbiTargetPlatform is not null)
                        artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dbi, dbiTargetPlatform, targetArch, targetPlatform, SymbolProperties.Coreclr, BuildId));

                    if (currentPlatform == OSPlatform.Windows)
                    {
                        // If we are running from Windows, we can target Linux and OS X dumps. We do build cross-os, cross-architecture debug libraries to run on Windows x64 or x86
                        if (dacCurrentPlatform is not null)
                            artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dac, dacCurrentPlatform, currentArch, currentPlatform, SymbolProperties.Coreclr, BuildId));

                        if (dbiCurrentPlatform is not null)
                            artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dbi, dbiCurrentPlatform, currentArch, currentPlatform, SymbolProperties.Coreclr, BuildId));
                    }
                }
            }

            // Windows CLRDEBUGINFO resource
            IResourceNode? resourceNode = module.ResourceRoot?.GetChild("RCData")?.GetChild("CLRDEBUGINFO")?.Children.FirstOrDefault();
            if (resourceNode is not null)
            {
                CLR_DEBUG_RESOURCE resource = resourceNode.Read<CLR_DEBUG_RESOURCE>(0);
                if (resource.dwVersion == 0)
                {
                    if (dacTargetPlatform is not null && resource.dwDacTimeStamp != 0 && resource.dwDacSizeOfImage != 0)
                        artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dac, dacTargetPlatform, targetArch, SymbolProperties.Self, resource.dwDacSizeOfImage, resource.dwDacTimeStamp));

                    if (dbiTargetPlatform is not null && resource.dwDbiTimeStamp != 0 && resource.dwDbiSizeOfImage != 0)
                        artifacts.Add(new DebugLibraryInfo(DebugLibraryKind.Dbi, dbiTargetPlatform, targetArch, SymbolProperties.Self, resource.dwDbiSizeOfImage, resource.dwDbiTimeStamp));
                }
            }

            // Do NOT take a dependency on the order of enumerated libraries.  I reserve the right to change this at any time.
            IOrderedEnumerable<DebugLibraryInfo> ordered = from artifact in EnumerateUnique(artifacts)
                                                           orderby artifact.Kind,
                                                                   Path.GetFileName(artifact.FileName) == artifact.FileName, // if we have a full local path, put it first
                                                                   artifact.ArchivedUnder
                                                           select artifact;

            DebuggingLibraries = ordered.ToImmutableArray();
        }

        private static IEnumerable<DebugLibraryInfo> EnumerateUnique(List<DebugLibraryInfo> artifacts)
        {
            HashSet<DebugLibraryInfo> seen = new();

            foreach (DebugLibraryInfo library in artifacts)
                if (seen.Add(library))
                    yield return library;
        }

        private static string GetWindowsLongNameDac(ClrFlavor flavor, Architecture currentArchitecture, Architecture targetArchitecture, Version version)
        {
            string dacNameBase = flavor == ClrFlavor.Core ? c_coreDacFileNameBase : c_desktopDacFileNameBase;
            return $"{dacNameBase}_{ArchitectureToName(currentArchitecture)}_{ArchitectureToName(targetArchitecture)}_{version.Major}.{version.Minor}.{version.Build}.{version.Revision:D2}.dll".ToLowerInvariant();
        }

        private static string ArchitectureToName(Architecture arch)
        {
            return arch switch
            {
                Architecture.X64 => "amd64",
                _ => arch.ToString()
            };
        }

        internal static ClrInfo? TryCreate(DataTarget dataTarget, ModuleInfo module)
        {
            if (dataTarget is null)
                throw new ArgumentNullException(nameof(dataTarget));

            if (module is null)
                throw new ArgumentNullException(nameof(module));

            if (IsSupportedRuntime(module, out ClrFlavor flavor))
                return new ClrInfo(dataTarget, flavor, module, 0);

            if ((dataTarget.DataReader.TargetPlatform != OSPlatform.Windows) || Path.GetExtension(module.FileName).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ulong singleFileRuntimeInfo = module.GetExportSymbolAddress(ClrRuntimeInfo.SymbolValue);
                if (singleFileRuntimeInfo != 0)
                    return new ClrInfo(dataTarget, ClrFlavor.Core, module, singleFileRuntimeInfo);
            }

            return null;
        }

        private static string? GetDbiFileName(ClrFlavor flavor, OSPlatform targetPlatform)
        {
            if (flavor == ClrFlavor.Core)
            {
                if (targetPlatform == OSPlatform.Windows)
                    return c_windowsDbiFileName;
                else if (targetPlatform == OSPlatform.Linux)
                    return c_linuxCoreDbiFileName;
                else if (targetPlatform == OSPlatform.OSX)
                    return c_macOSCoreDbiFileName;
            }

            if (flavor == ClrFlavor.Desktop)
            {
                if (targetPlatform == OSPlatform.Windows)
                    return c_windowsDbiFileName;
            }

            return null;
        }

        private static string? GetDacFileName(ClrFlavor flavor, OSPlatform targetPlatform)
        {
            if (flavor == ClrFlavor.Core)
            {
                if (targetPlatform == OSPlatform.Windows)
                    return c_coreDacFileName;
                else if (targetPlatform == OSPlatform.Linux)
                    return c_linuxCoreDacFileName;
                else if (targetPlatform == OSPlatform.OSX)
                    return c_macOSCoreDacFileName;
            }

            if (flavor == ClrFlavor.Desktop)
            {
                if (targetPlatform == OSPlatform.Windows)
                    return c_desktopDacFileName;
            }

            return null;
        }

        private static bool IsSupportedRuntime(ModuleInfo module, out ClrFlavor flavor)
        {
            flavor = default;

            string moduleName = Path.GetFileName(module.FileName);
            if (moduleName.Equals(c_desktopModuleName, StringComparison.OrdinalIgnoreCase))
            {
                flavor = ClrFlavor.Desktop;
                return true;
            }

            if (moduleName.Equals(c_coreModuleName, StringComparison.OrdinalIgnoreCase))
            {
                flavor = ClrFlavor.Core;
                return true;
            }

            if (moduleName.Equals(c_macOSCoreModuleName, StringComparison.OrdinalIgnoreCase))
            {
                flavor = ClrFlavor.Core;
                return true;
            }

            if (moduleName.Equals(c_linuxCoreModuleName, StringComparison.Ordinal))
            {
                flavor = ClrFlavor.Core;
                return true;
            }

            return false;
        }

        public DataTarget DataTarget { get; }

        IDataTarget IClrInfo.DataTarget => DataTarget;

        /// <summary>
        /// Gets the version number of this runtime.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// Returns whether this CLR was built as a single file executable.
        /// </summary>
        public bool IsSingleFile { get; }

        /// <summary>
        /// Gets the type of CLR this module represents.
        /// </summary>
        public ClrFlavor Flavor { get; }

        /// <summary>
        /// A list of debugging libraries associated associated with this .Net runtime.
        /// This can contain both the dac (used by ClrMD) and the DBI (not used by ClrMD).
        /// </summary>
        public ImmutableArray<DebugLibraryInfo> DebuggingLibraries { get; }

        /// <summary>
        /// Gets module information about the ClrInstance.
        /// </summary>
        public ModuleInfo ModuleInfo { get; }

        /// <summary>
        /// The timestamp under which this CLR is is archived (0 if this module is indexed under
        /// a BuildId instead).  Note that this may be a different value from ModuleInfo.IndexTimeStamp.
        /// In a single-file scenario, the ModuleInfo will be the info of the program's main executable
        /// and not CLR's properties.
        /// </summary>
        public int IndexTimeStamp { get; }

        /// <summary>
        /// The filesize under which this CLR is is archived (0 if this module is indexed under
        /// a BuildId instead).  Note that this may be a different value from ModuleInfo.IndexFileSize.
        /// In a single-file scenario, the ModuleInfo will be the info of the program's main executable
        /// and not CLR's properties.
        /// </summary>
        public int IndexFileSize { get; }

        /// <summary>
        /// The BuildId under which this CLR is archived.  BuildId.IsEmptyOrDefault will be true if
        /// this runtime is archived under file/timesize instead.
        /// </summary>
        public ImmutableArray<byte> BuildId { get; } = ImmutableArray<byte>.Empty;

        /// <summary>
        /// To string.
        /// </summary>
        /// <returns>A version string for this CLR.</returns>
        public override string ToString() => Version.ToString();

        /// <summary>
        /// Creates a runtime from the DacLibrary.
        /// </summary>
        /// <param name="dacLibrary">A fully constructed DacLibrary to use.</param>
        /// <returns>The runtime associated with this CLR.</returns>
        public ClrRuntime CreateRuntime(DacLibrary dacLibrary)
        {
            if (IntPtr.Size != DataTarget.DataReader.PointerSize)
                throw new InvalidOperationException("Mismatched pointer size between this process and the dac.");

            return new ClrRuntime(this, dacLibrary);
        }

        /// <summary>
        /// Creates a runtime from the given DAC file on disk.  This is equivalent to
        /// CreateRuntime(dacPath, ignoreMismatch: false).
        /// </summary>
        /// <param name="dacPath">A full path to the matching DAC dll for this process.</param>
        /// <returns>The runtime associated with this CLR.</returns>
        public ClrRuntime CreateRuntime(string dacPath) => CreateRuntime(dacPath, ignoreMismatch: false);

        /// <summary>
        /// Creates a runtime from the given DAC file on disk.
        /// </summary>
        /// <param name="dacPath">A full path to the matching DAC dll for this process.</param>
        /// <param name="ignoreMismatch">Whether or not to ignore mismatches between. </param>
        /// <returns>The runtime associated with this CLR.</returns>
        public ClrRuntime CreateRuntime(string dacPath, bool ignoreMismatch)
        {
            if (string.IsNullOrEmpty(dacPath))
                throw new ArgumentNullException(nameof(dacPath));

            if (!File.Exists(dacPath))
                throw new FileNotFoundException(dacPath);

            if (!ignoreMismatch && !IsSingleFile)
            {
                DataTarget.PlatformFunctions.GetFileVersion(dacPath, out int major, out int minor, out int revision, out int patch);
                if (major != Version.Major || minor != Version.Minor || revision != Version.Build || patch != Version.Revision)
                    throw new ClrDiagnosticsException($"Mismatched dac. Dac version: {major}.{minor}.{revision}.{patch}, expected: {Version}.");
            }

            DacLibrary dacLibrary = new(DataTarget, dacPath, ModuleInfo.ImageBase);
            return CreateRuntime(dacLibrary);
        }

        /// <summary>
        /// Creates a runtime by searching for the correct dac to load.
        /// </summary>
        /// <returns>The runtime associated with this CLR.</returns>
        public ClrRuntime CreateRuntime()
        {
            if (IntPtr.Size != DataTarget.DataReader.PointerSize)
                throw new InvalidOperationException("Mismatched pointer size between this process and the dac.");

            OSPlatform currentPlatform = GetCurrentPlatform();
            Architecture currentArch = RuntimeInformation.ProcessArchitecture;

            string? dacPath = null;
            bool foundOne = false;
            Exception? exception = null;

            IFileLocator? locator = DataTarget.FileLocator;

            foreach (DebugLibraryInfo dac in DebuggingLibraries.Where(r => r.Kind == DebugLibraryKind.Dac && r.Platform == currentPlatform && r.TargetArchitecture == currentArch))
            {
                foundOne = true;

                // If we have a full path, use it.  We already validated that the CLR matches.
                if (Path.GetFileName(dac.FileName) != dac.FileName)
                {
                    dacPath = dac.FileName;
                }
                else
                {
                    // The properties we are requesting under may not be the actual file properties, so don't request them.

                    if (locator != null)
                    {
                        if (!dac.IndexBuildId.IsDefaultOrEmpty)
                        {
                            if (dac.Platform == OSPlatform.Windows)
                                dacPath = locator.FindPEImage(dac.FileName, SymbolProperties.Coreclr, dac.IndexBuildId, DataTarget.DataReader.TargetPlatform, checkProperties: false);
                            else if (dac.Platform == OSPlatform.Linux)
                                dacPath = locator.FindElfImage(dac.FileName, SymbolProperties.Coreclr, dac.IndexBuildId, checkProperties: false);
                            else if (dac.Platform == OSPlatform.OSX)
                                dacPath = locator.FindMachOImage(dac.FileName, SymbolProperties.Coreclr, dac.IndexBuildId, checkProperties: false);
                        }
                        else if (dac.IndexTimeStamp != 0 && dac.IndexFileSize != 0)
                        {
                            if (dac.Platform == OSPlatform.Windows)
                                dacPath = DataTarget.FileLocator?.FindPEImage(dac.FileName, dac.IndexTimeStamp, dac.IndexFileSize, checkProperties: false);
                        }
                    }
                }

                if (dacPath is not null && File.Exists(dacPath))
                {
                    try
                    {
                        return CreateRuntime(dacPath, ignoreMismatch: true);
                    }
                    catch (Exception ex)
                    {
                        exception ??= ex;
                        dacPath = null;
                    }
                }
            }

            if (exception is not null)
                throw exception;

            // We should have had at least one dac enumerated if this is a supported scenario.
            if (!foundOne)
                ThrowCrossDebugError(currentPlatform);

            throw new FileNotFoundException("Could not find matching DAC for this runtime.");
        }

        IClrRuntime IClrInfo.CreateRuntime() => CreateRuntime();

        IClrRuntime IClrInfo.CreateRuntime(DacLibrary dacLibrary) => CreateRuntime(dacLibrary);

        IClrRuntime IClrInfo.CreateRuntime(string dacPath) => CreateRuntime(dacPath);

        IClrRuntime IClrInfo.CreateRuntime(string dacPath, bool ignoreMismatch) => CreateRuntime(dacPath, ignoreMismatch);

        private static OSPlatform GetCurrentPlatform()
        {
            OSPlatform currentPlatform;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                currentPlatform = OSPlatform.Windows;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                currentPlatform = OSPlatform.Linux;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                currentPlatform = OSPlatform.OSX;
            else
                throw new PlatformNotSupportedException();
            return currentPlatform;
        }

        private void ThrowCrossDebugError(OSPlatform current)
        {
            throw new InvalidOperationException($"Debugging a '{DataTarget.DataReader.TargetPlatform}' crash is not supported on '{current}'.");
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CLR_DEBUG_RESOURCE
        {
            public uint dwVersion;
            public Guid signature;
            public int dwDacTimeStamp;
            public int dwDacSizeOfImage;
            public int dwDbiTimeStamp;
            public int dwDbiSizeOfImage;
        }
    }
}