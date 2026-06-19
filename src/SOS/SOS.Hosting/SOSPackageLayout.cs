// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Shared;

/// <summary>
/// Describes the on-disk layout of the diagnostics tool packages (dotnet-dump,
/// dotnet-sos, SOS.Package, dbgshim) relative to the assembly this type is compiled
/// into.
///
/// A package contains two kinds of binaries that are loaded by the analyzer host:
/// <list type="bullet">
/// <item><description>
/// Native binaries that are loaded directly into the analyzer host process and
/// therefore must match the host's OS and processor architecture (sos.dll /
/// libsosplugin, <c>mscordaccore_universal</c>, <c>DiaSymReader</c>, …). They live
/// in a host-specific subfolder of the managed assembly directory (e.g.
/// <c>tools/&lt;tfm&gt;/any/win-x64/</c>). The folder name is the analyzer host's
/// runtime identifier — that's the layout the build emits.
/// </description></item>
/// <item><description>
/// SOS managed extension assemblies that <c>dotnet-sos install</c> copies into the
/// SOS install directory (Microsoft.Diagnostics.ExtensionCommands.dll, etc.). They
/// live in the <c>lib/</c> sibling subfolder and are not OS/arch specific.
/// </description></item>
/// </list>
///
/// This file is compile-included by <c>SOS.Hosting</c> and <c>SOS.InstallHelper</c>
/// so they agree on the layout. Because it is compiled into each consumer, the
/// package base directory is the directory of the consuming assembly.
/// </summary>
internal static class SOSPackageLayout
{
    /// <summary>
    /// The directory of the package this type is compiled into. All package-relative
    /// paths are rooted here.
    /// </summary>
    private static string s_packageBaseDirectory = ComputePackageBaseDirectory();

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
    /// Returns the directory containing the SOS managed extension assemblies for this
    /// package.
    /// </summary>
    public static string GetManagedBinariesDirectory()
        => Path.Combine(s_packageBaseDirectory, "lib");

    private static string ComputePackageBaseDirectory()
    {
        string location = typeof(SOSPackageLayout).Assembly.Location;
        return Path.GetDirectoryName(location)
            ?? throw new InvalidOperationException($"Cannot resolve package base directory: {typeof(SOSPackageLayout).Assembly.GetName().Name} has no on-disk location.");
    }

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
