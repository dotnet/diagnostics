// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
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

            // The cDAC (mscordaccore_universal) ships inside the (signed) diagnostics tool package and
            // carries no individual DAC signature, so it cannot satisfy ClrMD's signature check. Trust it
            // the same way the native and SOS-hosting cDAC load paths do (load without verification), while
            // still verifying the in-box DAC. We trust ONLY the exact cDAC path the host resolver provides
            // (the bundled binary next to sos); matching by file name alone would let a name-hijacked DLL
            // loaded from elsewhere (target runtime dir, symbol cache, ...) bypass verification.
            string trustedCDacPath = _services.GetService<IHostAssetResolver>()?.GetCDacPath();
            string normalizedTrustedCDacPath = string.IsNullOrEmpty(trustedCDacPath) ? null : Path.GetFullPath(trustedCDacPath);
            StringComparison pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            DataTarget dataTarget = new(_services.GetService<IDataReader>(), new DataTargetOptions()
            {
                ForceCompleteRuntimeEnumeration = (flags & RuntimeEnumerationFlags.All) != 0,
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
                }
            });
            for (int i = 0; i < dataTarget.ClrVersions.Length; i++)
            {
                yield return new Runtime(_services, startingRuntimeId + i, dataTarget.ClrVersions[i]);
            }
        }

        #endregion
    }
}
