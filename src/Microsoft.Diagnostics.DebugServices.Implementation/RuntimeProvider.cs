// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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
            ISettingsService settingsService = _services.GetService<ISettingsService>();
            // The ClrInfo and DataTarget instances are disposed when Runtime instance is disposed. Runtime instances are
            // not flushed when the Target/RuntimeService is flushed; they are all disposed and the list cleared. They are
            // all re-created the next time the IRuntime or ClrRuntime instance is queried.
            DataTarget dataTarget = new(new CustomDataTarget(_services.GetService<IDataReader>())
            {
                ForceCompleteRuntimeEnumeration = (flags & RuntimeEnumerationFlags.All) != 0,
                DacSignatureVerificationEnabled = settingsService is null || settingsService.DacSignatureVerificationEnabled,
                FileLocator = null
            });
            for (int i = 0; i < dataTarget.ClrVersions.Length; i++)
            {
                yield return new Runtime(_services, startingRuntimeId + i, dataTarget.ClrVersions[i]);
            }
        }

        #endregion
    }
}
