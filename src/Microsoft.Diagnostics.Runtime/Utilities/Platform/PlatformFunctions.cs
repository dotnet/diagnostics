// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A set of helper functions that are consistently implemented across platforms.
    /// </summary>
    public abstract class PlatformFunctions
    {
        internal static readonly byte[] s_versionString = Encoding.ASCII.GetBytes("@(#)Version ");
        internal static readonly int s_versionLength = s_versionString.Length;

        internal abstract bool GetFileVersion(string dll, out int major, out int minor, out int revision, out int patch);
        public abstract bool TryGetWow64(IntPtr proc, out bool result);

        /// <param name="libraryPath">The path to the native library to be loaded.</param>
        public abstract IntPtr LoadLibrary(string libraryPath);

        /// <param name="handle">The native library OS handle to be freed.</param>
        public abstract bool FreeLibrary(IntPtr handle);

        /// <param name="handle">The native library OS handle.</param>
        /// <param name="name">The name of the exported symbol.</param>
        public abstract IntPtr GetLibraryExport(IntPtr handle, string name);

        public virtual bool IsEqualFileVersion(string file, Version version)
        {
            if (!GetFileVersion(file, out int major, out int minor, out int build, out int revision))
            {
                if (version == null)
                    return true;  // the runtime module has no version info either

                return false;
            }

            return major == version.Major && minor == version.Minor && build == version.Build && revision == version.Revision;
        }
    }
}
