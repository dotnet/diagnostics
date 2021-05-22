// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
    }
}
