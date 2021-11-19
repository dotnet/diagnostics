// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Provides module info
    /// </summary>
    public interface IModuleService
    {
        /// <summary>
        /// Enumerate all the modules in the target
        /// </summary>
        IEnumerable<IModule> EnumerateModules();

        /// <summary>
        /// Get the module from the module index
        /// </summary>
        /// <param name="moduleIndex">index</param>
        /// <returns>module</returns>
        /// <exception cref="DiagnosticsException">invalid module index</exception>
        IModule GetModuleFromIndex(int moduleIndex);

        /// <summary>
        /// Get the module from the module base address
        /// </summary>
        /// <param name="baseAddress">module address</param>
        /// <returns>module</returns>
        /// <exception cref="DiagnosticsException">base address not found</exception>
        IModule GetModuleFromBaseAddress(ulong baseAddress);

        /// <summary>
        /// Finds the module that contains the address.
        /// </summary>
        /// <param name="address">search address</param>
        /// <returns>module or null</returns>
        IModule GetModuleFromAddress(ulong address);

        /// <summary>
        /// Finds the module(s) with the specified module name. It is the platform dependent
        /// name that includes the "lib" prefix on xplat and the extension (dll, so or dylib).
        /// </summary>
        /// <param name="moduleName">module name to find</param>
        /// <returns>matching modules</returns>
        IEnumerable<IModule> GetModuleFromModuleName(string moduleName);
    }
}
