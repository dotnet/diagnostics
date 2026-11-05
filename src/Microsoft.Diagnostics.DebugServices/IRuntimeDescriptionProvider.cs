// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Discovers runtimes and captures their metadata without creating inspection runtimes.
    /// Providers can be registered through ProviderExport or host service registration.
    /// </summary>
    public interface IRuntimeDescriptionProvider
    {
        /// <summary>
        /// Higher priorities are tried first for hinted-module and fallback discovery.
        /// The built-in normal .NET provider runs before those probes, regardless of priority.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Recognizes one module and captures its metadata. The first successful provider wins.
        /// The description must refer to the module being probed.
        /// </summary>
        bool TryDescribeRuntime(IModule module, out RuntimeDescription description);
    }
}
