// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting
{
    /// <summary>
    /// Constants to return from GetPlatform
    /// </summary>
    public enum CorDebugPlatform
    {
        CORDB_PLATFORM_WINDOWS_X86 = 0, // Windows on Intel x86
        CORDB_PLATFORM_WINDOWS_AMD64 = 1, // Windows x64 (Amd64, Intel EM64T)
        CORDB_PLATFORM_WINDOWS_IA64 = 2, // Windows on Intel IA-64
        CORDB_PLATFORM_MAC_PPC = 3, // MacOS on PowerPC
        CORDB_PLATFORM_MAC_X86 = 4, // MacOS on Intel x86
        CORDB_PLATFORM_WINDOWS_ARM = 5, // Windows on ARM
        CORDB_PLATFORM_MAC_AMD64 = 6,
        CORDB_PLATFORM_WINDOWS_ARM64 = 7, // Windows on ARM64
        CORDB_PLATFORM_POSIX_AMD64 = 8,
        CORDB_PLATFORM_POSIX_X86 = 9,
        CORDB_PLATFORM_POSIX_ARM = 10,
        CORDB_PLATFORM_POSIX_ARM64 = 11
    }
}
