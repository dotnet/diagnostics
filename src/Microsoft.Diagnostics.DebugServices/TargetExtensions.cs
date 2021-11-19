// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class TargetExtensions
    {
        /// <summary>
        /// Returns the decorated platform specific module name. "coreclr" becomes "coreclr.dll"
        /// for Windows targets, "libcoreclr.so" for Linux targets, etc.
        /// </summary>
        /// <param name="target">target instance</param>
        /// <param name="moduleName">base module name</param>
        /// <returns>platform module name</returns>
        public static string GetPlatformModuleName(this ITarget target, string moduleName)
        {
            if (target.OperatingSystem == OSPlatform.Windows)
            {
                return moduleName + ".dll";
            }
            else if (target.OperatingSystem == OSPlatform.Linux)
            {
                return "lib" + moduleName + ".so";
            }
            else if (target.OperatingSystem == OSPlatform.OSX)
            {
                return "lib" + moduleName + ".dylib";
            }
            throw new PlatformNotSupportedException(target.OperatingSystem.ToString());
        }

        /// <summary>
        /// Registers an object to be disposed when target is destroyed.
        /// </summary>
        /// <param name="target">target instance</param>
        /// <param name="disposable">object to be disposed or null</param>
        /// <returns>IDisposable to unregister this event or null</returns>
        public static IDisposable DisposeOnDestroy(this ITarget target, IDisposable disposable)
        {
            return disposable != null ? target.OnDestroyEvent.Register(() => disposable.Dispose()) : null;
        }
    }
}
