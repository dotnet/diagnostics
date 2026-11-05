// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Converts runtime descriptions into IRuntime instances using ClrMD.
    /// </summary>
    [ProviderExport(Type = typeof(IRuntimeProvider))]
    public class DescriptionBasedRuntimeProvider : IRuntimeProvider
    {
        private readonly IServiceProvider _services;

        public DescriptionBasedRuntimeProvider(IServiceProvider services)
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
            ISettingsService settingsService = _services.GetService<ISettingsService>();
            bool verifyDac = settingsService?.DacSignatureVerificationEnabled ?? true;
            IDataReader dataReader = _services.GetService<IDataReader>();
            // The cDAC (mscordaccore_universal) ships inside the (signed) diagnostics tool package and
            // carries no individual DAC signature, so it cannot satisfy ClrMD's signature check. Trust it
            // the same way the native and SOS-hosting cDAC load paths do (load without verification), while
            // still verifying the in-box DAC. We trust ONLY the exact cDAC path the host resolver provides
            // (the bundled binary next to sos); matching by file name alone would let a name-hijacked DLL
            // loaded from elsewhere (target runtime dir, symbol cache, ...) bypass verification.
            string trustedCDacPath = _services.GetService<IHostAssetResolver>()?.GetCDacPath();
            string normalizedTrustedCDacPath = string.IsNullOrEmpty(trustedCDacPath) ? null : Path.GetFullPath(trustedCDacPath);
            StringComparison pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            IRuntimeDescriptionProvider[] providers = _services.GetService<IServiceManager>()
                .EnumerateProviderFactories(typeof(IRuntimeDescriptionProvider))
                .Select(factory => (IRuntimeDescriptionProvider)factory(_services))
                .ToArray();
            DataTarget dataTarget = new(dataReader, new DataTargetOptions
            {
                SkipRuntimeEnumeration = true,
                VerifyDacOnWindows = verifyDac,
                // Takes priority over VerifyDacOnWindows: skip verification only for the exact bundled cDAC.
                DacSignatureVerificationOverride = (dacFilePath) => {
                    if (normalizedTrustedCDacPath is not null
                        && !string.IsNullOrEmpty(dacFilePath)
                        && string.Equals(Path.GetFullPath(dacFilePath), normalizedTrustedCDacPath, pathComparison))
                    {
                        return false;
                    }
                    return verifyDac;
                },
                SymbolProvider = _services.GetService<IClrSymbolProvider>(),
            });
            // The enumerator and each yielded runtime own a reference to the shared, complete target.
            int referenceCount = 1;
            Action releaseDataTarget = () => {
                if (Interlocked.Decrement(ref referenceCount) == 0)
                {
                    dataTarget.Dispose();
                }
            };

            try
            {
                Dictionary<ulong, ModuleInfo> modules = dataTarget.EnumerateModules().ToDictionary(module => module.ImageBase);
                foreach (RuntimeDescription description in EnumerateRuntimeDescriptions(providers, flags))
                {
                    if (description.Module.Target != _services.GetService<ITarget>() ||
                        !modules.TryGetValue(description.Module.ImageBase, out ModuleInfo module))
                    {
                        throw new DiagnosticsException($"Discovered runtime module {description.Module.ImageBase:X16} is not in the target module list");
                    }
                    dataTarget.AddRuntime(ClrInfoConverter.Create(dataTarget, module, description));
                }

                int id = startingRuntimeId;
                foreach (ClrInfo clrInfo in dataTarget.ClrVersions.OrderByDescending(runtime => runtime.Flavor).ThenBy(runtime => runtime.Version))
                {
                    Runtime runtime = new(_services, id++, clrInfo, releaseDataTarget);
                    Interlocked.Increment(ref referenceCount);
                    yield return runtime;
                }
            }
            finally
            {
                releaseDataTarget();
            }
        }

        #endregion

        internal IEnumerable<RuntimeDescription> EnumerateRuntimeDescriptions(
            IReadOnlyList<IRuntimeDescriptionProvider> providers, RuntimeEnumerationFlags flags)
        {
            if (providers.Count == 0)
            {
                yield break;
            }
            // Hinted and fallback modules try providers in priority order; ties are unspecified.
            IRuntimeDescriptionProvider[] orderedProviders = providers
                .OrderByDescending(provider => provider.Priority)
                .ToArray();
            bool all = (flags & RuntimeEnumerationFlags.All) != 0;
            IModule[] modules = _services.GetService<IModuleService>().EnumerateModules().ToArray();
            HashSet<ulong> handledModules = [];
            bool foundRuntime = false;

            foreach (RuntimeDescription description in EnumeratePriorityRuntimeDescriptions(orderedProviders, modules, handledModules))
            {
                foundRuntime = true;
                yield return description;
            }

            if (foundRuntime && !all)
            {
                yield break;
            }

            foreach (RuntimeDescription description in EnumerateFallbackRuntimeDescriptions(orderedProviders, modules, handledModules, all))
            {
                yield return description;
            }
        }

        private IEnumerable<RuntimeDescription> EnumeratePriorityRuntimeDescriptions(
            IReadOnlyList<IRuntimeDescriptionProvider> providers, IReadOnlyList<IModule> modules, HashSet<ulong> handledModules)
        {
            foreach (DotNetRuntimeDiscoveryProvider provider in providers.OfType<DotNetRuntimeDiscoveryProvider>())
            {
                foreach (IModule module in modules)
                {
                    if (handledModules.Contains(module.ImageBase) ||
                        !TryDescribeRuntime(provider, module, out RuntimeDescription description))
                    {
                        continue;
                    }
                    handledModules.Add(module.ImageBase);
                    yield return description;
                }
            }

            foreach (IModule module in EnumerateHintedModules(modules))
            {
                // Hints are fully handled here, even if no provider matches.
                if (!handledModules.Add(module.ImageBase))
                {
                    continue;
                }
                foreach (IRuntimeDescriptionProvider provider in providers.Where(provider => provider is not DotNetRuntimeDiscoveryProvider))
                {
                    if (!TryDescribeRuntime(provider, module, out RuntimeDescription description))
                    {
                        continue;
                    }
                    yield return description;
                    break;
                }
            }
        }

        private static IEnumerable<RuntimeDescription> EnumerateFallbackRuntimeDescriptions(
            IReadOnlyList<IRuntimeDescriptionProvider> providers, IReadOnlyList<IModule> modules, HashSet<ulong> handledModules, bool all)
        {
            foreach (IRuntimeDescriptionProvider provider in providers.Where(provider => provider is not DotNetRuntimeDiscoveryProvider))
            {
                foreach (IModule module in modules)
                {
                    if (handledModules.Contains(module.ImageBase) ||
                        !TryDescribeRuntime(provider, module, out RuntimeDescription description))
                    {
                        continue;
                    }
                    handledModules.Add(module.ImageBase);
                    yield return description;
                    if (!all)
                    {
                        yield break;
                    }
                }
            }
        }

        private static bool TryDescribeRuntime(
            IRuntimeDescriptionProvider provider, IModule module, out RuntimeDescription description)
        {
            if (!provider.TryDescribeRuntime(module, out description))
            {
                return false;
            }
            if (description is null || description.Module.Target != module.Target || description.Module.ImageBase != module.ImageBase)
            {
                throw new DiagnosticsException($"Discovered runtime description does not match the probed target module {module.ImageBase:X16}");
            }
            return true;
        }

        private IEnumerable<IModule> EnumerateHintedModules(IReadOnlyList<IModule> modules)
        {
            IModuleService moduleService = _services.GetService<IModuleService>();
            Dictionary<ulong, IModule> modulesByBase = modules.ToDictionary(module => module.ImageBase);
            ulong crashRuntimeBase = _services.GetService<ICrashInfoService>()?.RuntimeBaseAddress ?? 0;
            if (modulesByBase.TryGetValue(crashRuntimeBase, out IModule crashModule))
            {
                yield return crashModule;
            }

            IDataReader reader = _services.GetService<IDataReader>();
            if (reader.TargetPlatform != OSPlatform.Windows &&
                SpecialDiagInfo.TryGetRuntimeBaseAddress(reader, out ulong runtimeBase) &&
                modulesByBase.TryGetValue(runtimeBase, out IModule runtimeModule))
            {
                yield return runtimeModule;
            }
            if (reader.TargetPlatform == OSPlatform.Windows &&
                moduleService.EntryPointModule is IModule entryPoint)
            {
                yield return entryPoint;
            }
        }
    }
}
