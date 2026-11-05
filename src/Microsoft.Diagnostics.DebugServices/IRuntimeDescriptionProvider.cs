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
        /// Higher priority description providers are attempted first.
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Attempts to recognize the runtime in the module represented by <see cref="IModule"/>.
        /// </summary>
        /// <returns>True if the runtime was successfully recognized; otherwise, false.</returns>
        bool TryDescribeRuntime(IModule module, out RuntimeDescription description);
    }
}
