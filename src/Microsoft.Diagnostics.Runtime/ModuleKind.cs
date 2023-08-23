// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The kind of module a <see cref="ModuleInfo"/> represents.
    /// </summary>
    public enum ModuleKind
    {
        /// <summary>
        /// Default value, should not be returned.
        /// </summary>
        Unknown,

        /// <summary>
        /// This module is not one of the other well defined types but isn't a part of this enum.
        /// </summary>
        Other,

        /// <summary>
        /// A Windows PortableExecutable file.
        /// </summary>
        PortableExecutable,

        /// <summary>
        /// An Elf image (usually Linux).
        /// </summary>
        Elf,

        /// <summary>
        /// An OS X Mach-O image.
        /// </summary>
        MachO
    }
}