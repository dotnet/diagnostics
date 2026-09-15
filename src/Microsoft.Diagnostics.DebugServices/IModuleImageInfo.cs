// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides the availability of image metadata for a module.
    /// </summary>
    public interface IModuleImageInfo
    {
        /// <summary>
        /// Gets whether the image metadata needed to determine module characteristics is available.
        /// When false, properties such as <see cref="IModule.IsManaged"/> and
        /// <see cref="IModule.IsFileLayout"/> may contain fallback values.
        /// </summary>
        bool IsImageInfoAvailable { get; }
    }
}
