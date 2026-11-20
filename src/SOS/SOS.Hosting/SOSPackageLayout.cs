// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Shared;

/// <summary>
/// Describes the on-disk layout of the diagnostics tool packages (dotnet-dump,
/// dotnet-sos, SOS.Package, dbgshim) relative to <see cref="AppContext.BaseDirectory"/>.
///
/// Native binaries loaded into the analyzer host process must match the host's OS
/// and processor architecture. They live in a host-specific subfolder of the managed
/// assembly directory (for example, <c>tools/&lt;tfm&gt;/any/win-x64/</c>). The
/// folder contains native SOS, its self-contained NativeAOT extension library, and
/// supporting native assets.
///
/// This file is compile-included by <c>SOS.Hosting</c> and <c>SOS.InstallHelper</c>
/// so they agree on the managed tool package layout. Native debugger hosts such as
/// CDB and LLDB provide the directory of the loaded SOS module through
/// <c>SOSLibrary.ISOSModule</c> and do not use this fallback.
/// </summary>
internal static class SOSPackageLayout
{
    /// <summary>
    /// The package root for managed tools that load SOS themselves. This remains
    /// available when the consuming assembly is bundled into a single-file
    /// application and has no on-disk location.
    /// </summary>
    private static readonly string s_packageBaseDirectory = AppContext.BaseDirectory;

    /// <summary>
    /// Returns the directory containing the native binaries for this package,
    /// targeting <paramref name="architecture"/> (or the current host's architecture
    /// when <paramref name="architecture"/> is <c>null</c>). Used when the host needs
    /// to address binaries for a non-current architecture (for example, when
    /// <c>dotnet-sos install -a arm64</c> is invoked from an x64 host).
    /// </summary>
    public static string GetNativeBinariesDirectory(Architecture? architecture = null)
        => Path.Combine(s_packageBaseDirectory, GetHostNativeBinariesFolderName(architecture));

    /// <summary>
    /// The package-relative subfolder for native binaries targeting the current host
    /// OS and the supplied <paramref name="architecture"/> (or the current process
    /// architecture when <paramref name="architecture"/> is <c>null</c>).
    /// </summary>
    private static string GetHostNativeBinariesFolderName(Architecture? architecture)
        => ComputeHostNativeBinariesFolderName(architecture);

    private static string ComputeHostNativeBinariesFolderName(Architecture? architecture)
    {
        string os;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            os = "win";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            os = "osx";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            os = "linux";
            try
            {
                if (File.ReadAllText("/etc/os-release").Contains("ID=alpine"))
                {
                    os = "linux-musl";
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
            {
            }
        }
        else
        {
            throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
        }
        return $"{os}-{(architecture ?? RuntimeInformation.ProcessArchitecture).ToString().ToLowerInvariant()}";
    }
}
