// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// The type of ELF file.
    /// </summary>
    internal enum ElfHeaderType : ushort
    {
        Relocatable = 1,
        Executable = 2,
        Shared = 3,
        Core = 4
    }
}
