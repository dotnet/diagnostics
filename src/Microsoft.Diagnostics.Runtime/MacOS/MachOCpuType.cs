// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal enum MachOCpuType
    {
        X86 = 7,
        X86_64 = X86 | MachOCpuArch.Abi64,

        ARM = 12,
        ARM64 = ARM | MachOCpuArch.Abi64
    }
}