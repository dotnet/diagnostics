// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    internal sealed class RuntimeDescriptionBuilder
    {
        private readonly IServiceProvider _services;

        public RuntimeDescriptionBuilder(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public RuntimeDescription Create(IModule runtimeModule, ulong runtimeInfo, RuntimeType flavor)
        {
            IDataReader reader = _services.GetService<IDataReader>();
            Version version;
            int indexTimeStamp = 0;
            int indexFileSize = 0;
            ulong contractDescriptor = 0;
            ImmutableArray<byte> buildId = ImmutableArray<byte>.Empty;
            List<RuntimeDebuggingLibrary> artifacts = new(8);

            OSPlatform currentPlatform = GetCurrentPlatform();
            OSPlatform targetPlatform = reader.TargetPlatform;
            Architecture currentArch = RuntimeInformation.ProcessArchitecture;
            Architecture targetArch = reader.Architecture;

            string dacCurrentPlatform = GetDacFileName(flavor, currentPlatform);
            string dacTargetPlatform = GetDacFileName(flavor, targetPlatform);
            string dbiCurrentPlatform = GetDbiFileName(flavor, currentPlatform);
            string dbiTargetPlatform = GetDbiFileName(flavor, targetPlatform);
            if (runtimeInfo != 0)
            {
                if (!RuntimeInfo.TryRead(_services, runtimeInfo, out RuntimeInfo info) || !info.IsValid ||
                    !info.TryReadVersion(_services, runtimeInfo, out version))
                {
                    Trace.TraceWarning($"Invalid DotNetRuntimeInfo header at {runtimeInfo:X16} in {runtimeModule.FileName}");
                    return null;
                }
                version ??= new Version();
                if (targetPlatform == OSPlatform.Windows)
                {
                    (indexTimeStamp, indexFileSize) = info.RuntimePEIIndex;

                    if (dacTargetPlatform is not null)
                    {
                        (int timeStamp, int fileSize) = info.DacPEIndex;
                        if (timeStamp != 0 && fileSize != 0)
                        {
                            artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dac, dacTargetPlatform, targetArch, RuntimeDebuggingLibraryIndex.Self, fileSize, timeStamp));
                        }
                    }
                    if (dbiTargetPlatform is not null)
                    {
                        (int timeStamp, int fileSize) = info.DbiPEIndex;
                        if (timeStamp != 0 && fileSize != 0)
                        {
                            artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dbi, dbiTargetPlatform, targetArch, RuntimeDebuggingLibraryIndex.Self, fileSize, timeStamp));
                        }
                    }
                }
                else
                {
                    buildId = info.RuntimeBuildId;
                    if (dacTargetPlatform is not null)
                    {
                        ImmutableArray<byte> dacBuild = info.DacBuildId;
                        if (!dacBuild.IsDefaultOrEmpty)
                        {
                            artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.Dac, dacTargetPlatform, targetArch, targetPlatform, RuntimeDebuggingLibraryIndex.Self, dacBuild));
                        }
                    }
                    if (dbiTargetPlatform is not null)
                    {
                        ImmutableArray<byte> dbiBuild = info.DbiBuildId;
                        if (!dbiBuild.IsDefaultOrEmpty)
                        {
                            artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.Dbi, dbiTargetPlatform, targetArch, targetPlatform, RuntimeDebuggingLibraryIndex.Self, dbiBuild));
                        }
                    }
                }
            }
            else
            {
                indexTimeStamp = unchecked((int)runtimeModule.IndexTimeStamp.GetValueOrDefault(0));
                indexFileSize = unchecked((int)runtimeModule.IndexFileSize.GetValueOrDefault(0));
                try
                {
                    buildId = runtimeModule.BuildId;
                }
                catch (DiagnosticsException ex)
                {
                    Trace.TraceError($"RuntimeDescriptionBuilder.BuildId: {runtimeModule.ImageBase:X16} exception {ex.Message}");
                }
                version = Utilities.EmptyVersion;
                try
                {
                    version = runtimeModule.GetVersionData() ?? Utilities.EmptyVersion;
                }
                catch (DiagnosticsException ex)
                {
                    Trace.TraceError($"RuntimeDescriptionBuilder.Version: {runtimeModule.ImageBase:X16} exception {ex.Message}");
                }
            }

            if (targetPlatform == OSPlatform.Windows && version.Major != 0)
            {
                artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dac, GetWindowsLongNameDac(flavor, currentArch, targetArch, version), currentArch, RuntimeDebuggingLibraryIndex.Runtime, indexFileSize, indexTimeStamp));
            }

            if (targetPlatform == currentPlatform)
            {
                if (dacCurrentPlatform is not null)
                {
                    if (indexFileSize != 0 && indexTimeStamp != 0)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dac, dacCurrentPlatform, targetArch, RuntimeDebuggingLibraryIndex.Runtime, indexFileSize, indexTimeStamp));
                    }
                    if (!buildId.IsDefaultOrEmpty)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.Dac, dacCurrentPlatform, targetArch, targetPlatform, RuntimeDebuggingLibraryIndex.Runtime, buildId));
                    }
                }
                if (dbiCurrentPlatform is not null)
                {
                    if (indexFileSize != 0 && indexTimeStamp != 0)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dbi, dbiCurrentPlatform, targetArch, RuntimeDebuggingLibraryIndex.Runtime, indexFileSize, indexTimeStamp));
                    }
                    if (!buildId.IsDefaultOrEmpty)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.Dbi, dbiCurrentPlatform, targetArch, targetPlatform, RuntimeDebuggingLibraryIndex.Runtime, buildId));
                    }
                }
            }
            else
            {
                if (indexFileSize != 0 && indexTimeStamp != 0)
                {
                    if (dacTargetPlatform is not null)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dac, dacTargetPlatform, targetArch, RuntimeDebuggingLibraryIndex.Runtime, indexFileSize, indexTimeStamp));
                    }
                    if (dbiTargetPlatform is not null)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dbi, dbiTargetPlatform, targetArch, RuntimeDebuggingLibraryIndex.Runtime, indexFileSize, indexTimeStamp));
                    }
                }
                if (!buildId.IsDefaultOrEmpty)
                {
                    if (dacTargetPlatform is not null)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.Dac, dacTargetPlatform, targetArch, targetPlatform, RuntimeDebuggingLibraryIndex.Runtime, buildId));
                    }
                    if (dbiTargetPlatform is not null)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.Dbi, dbiTargetPlatform, targetArch, targetPlatform, RuntimeDebuggingLibraryIndex.Runtime, buildId));
                    }
                    if (currentPlatform == OSPlatform.Windows)
                    {
                        if (dacCurrentPlatform is not null)
                        {
                            artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.Dac, dacCurrentPlatform, currentArch, currentPlatform, RuntimeDebuggingLibraryIndex.Runtime, buildId));
                        }
                        if (dbiCurrentPlatform is not null)
                        {
                            artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.Dbi, dbiCurrentPlatform, currentArch, currentPlatform, RuntimeDebuggingLibraryIndex.Runtime, buildId));
                        }
                    }
                }
            }

            IResourceNode resourceRoot = ModuleInfo.TryCreateResourceRoot(
                reader, runtimeModule.ImageBase, runtimeModule.ImageSize, runtimeModule.IsFileLayout.GetValueOrDefault(false));
            IResourceNode resourceNode = resourceRoot?.GetChild("RCData")?.GetChild("CLRDEBUGINFO")?.Children.FirstOrDefault();
            if (resourceNode is not null)
            {
                ClrDebugResource resource = resourceNode.Read<ClrDebugResource>(0);
                if (resource.Version == 0)
                {
                    if (dacTargetPlatform is not null && resource.DacTimeStamp != 0 && resource.DacSizeOfImage != 0)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dac, dacTargetPlatform, targetArch, RuntimeDebuggingLibraryIndex.Self, resource.DacSizeOfImage, resource.DacTimeStamp));
                    }
                    if (dbiTargetPlatform is not null && resource.DbiTimeStamp != 0 && resource.DbiSizeOfImage != 0)
                    {
                        artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.Dbi, dbiTargetPlatform, targetArch, RuntimeDebuggingLibraryIndex.Self, resource.DbiSizeOfImage, resource.DbiTimeStamp));
                    }
                }
            }

            if (flavor == RuntimeType.NetCore)
            {
                contractDescriptor = GetExportAddress(runtimeModule, "DotNetRuntimeContractDescriptor");
                if (contractDescriptor != 0)
                {
                    string cdacName = GetCDacFileName(currentPlatform);
                    if (cdacName is not null)
                    {
                        cdacName = Path.Combine(AppContext.BaseDirectory, cdacName);
                        if (currentPlatform == OSPlatform.Windows)
                        {
                            artifacts.Add(RuntimeDebuggingLibrary.CreateWindows(RuntimeDebuggingLibraryKind.CDac, cdacName, targetArch, RuntimeDebuggingLibraryIndex.None, indexFileSize, indexTimeStamp));
                        }
                        else
                        {
                            artifacts.Add(RuntimeDebuggingLibrary.CreateWithBuildId(RuntimeDebuggingLibraryKind.CDac, cdacName, targetArch, currentPlatform, RuntimeDebuggingLibraryIndex.None, buildId));
                        }
                    }
                }
            }

            return new RuntimeDescription(runtimeModule, flavor, version)
            {
                DebuggingLibraries = artifacts.ToImmutableArray(),
                ContractDescriptorAddress = contractDescriptor,
                IndexFileSize = indexFileSize,
                IndexTimeStamp = indexTimeStamp,
                BuildId = buildId,
            };
        }

        internal static ulong GetExportAddress(IModule module, string export)
        {
            try
            {
                IExportSymbols symbols = module.Services.GetService<IExportSymbols>();
                return symbols is not null && symbols.TryGetSymbolAddress(export, out ulong address) ? address : 0;
            }
            catch (Exception ex) when
                (ex is IOException or
                 InvalidDataException or
                 ArgumentException or
                 OverflowException or
                 DiagnosticsException)
            {
                Trace.TraceWarning($"Export lookup failed for {export} in {module.FileName}: {ex.Message}");
                return 0;
            }
        }

        private static string GetWindowsLongNameDac(RuntimeType flavor, Architecture currentArchitecture, Architecture targetArchitecture, Version version)
        {
            string name = flavor == RuntimeType.NetCore ? "mscordaccore" : "mscordacwks";
            string current = currentArchitecture == Architecture.X64 ? "amd64" : currentArchitecture.ToString();
            string target = targetArchitecture == Architecture.X64 ? "amd64" : targetArchitecture.ToString();
            return $"{name}_{current}_{target}_{version.Major}.{version.Minor}.{version.Build}.{version.Revision:D2}.dll".ToLowerInvariant();
        }

        private static string GetDbiFileName(RuntimeType flavor, OSPlatform platform)
        {
            if (platform == OSPlatform.Windows)
            {
                return "mscordbi.dll";
            }
            if (flavor == RuntimeType.NetCore)
            {
                return platform == OSPlatform.Linux ? "libmscordbi.so" : platform == OSPlatform.OSX ? "libmscordbi.dylib" : null;
            }
            return null;
        }

        private static string GetDacFileName(RuntimeType flavor, OSPlatform platform)
        {
            if (platform == OSPlatform.Windows)
            {
                return flavor == RuntimeType.NetCore ? "mscordaccore.dll" : "mscordacwks.dll";
            }
            if (flavor == RuntimeType.NetCore)
            {
                return platform == OSPlatform.Linux ? "libmscordaccore.so" : platform == OSPlatform.OSX ? "libmscordaccore.dylib" : null;
            }
            return null;
        }

        private static string GetCDacFileName(OSPlatform platform) =>
            platform == OSPlatform.Windows ? "mscordaccore_universal.dll"
                : platform == OSPlatform.Linux ? "libmscordaccore_universal.so"
                : platform == OSPlatform.OSX ? "libmscordaccore_universal.dylib"
                : null;

        private static OSPlatform GetCurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return OSPlatform.Windows;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return OSPlatform.Linux;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return OSPlatform.OSX;
            }
            throw new PlatformNotSupportedException();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ClrDebugResource
        {
            public uint Version;
            public Guid Signature;
            public int DacTimeStamp;
            public int DacSizeOfImage;
            public int DbiTimeStamp;
            public int DbiSizeOfImage;
        }
    }
}
