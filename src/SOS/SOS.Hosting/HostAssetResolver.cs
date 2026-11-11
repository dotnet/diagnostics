// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Shared;

namespace SOS.Hosting;

/// <summary>
/// The host-registered <see cref="IHostAssetResolver"/>. Resolves assets (native binaries, the
/// bundled cDAC) to the directory the host actually loaded sos from. When a native debugger host
/// supplies its sos module location (<see cref="SOSLibrary.ISOSModule"/>) that location wins;
/// otherwise the host loaded sos itself (e.g. dotnet-dump) and the directory comes from this
/// tool's package layout. Either way the cDAC ships in that same directory.
/// </summary>
public sealed class HostAssetResolver : IHostAssetResolver
{
    private HostAssetResolver(string nativeBinariesDirectory)
    {
        NativeBinariesDirectory = nativeBinariesDirectory;
    }

    public string NativeBinariesDirectory { get; }

    private static readonly string s_cDACBinaryName = ComputeCDacHostBinaryName();

    private static string ComputeCDacHostBinaryName()
    {
        const string baseName = "mscordaccore_universal";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return baseName + ".dll";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "lib" + baseName + ".dylib";
        }
        return "lib" + baseName + ".so";
    }

    public string GetCDacPath()
    {
        return Path.Combine(NativeBinariesDirectory, s_cDACBinaryName);
    }

    [ServiceExport(Scope = ServiceScope.Global)]
    public static IHostAssetResolver Create([ServiceImport(Optional = true)] SOSLibrary.ISOSModule sosModule)
    {
        // A native debugger host that loaded sos for us is the authoritative source for where
        // the native binaries (and the cDAC next to them) live. Otherwise fall back to this
        // tool's package layout.
        string nativeBinariesDirectory = sosModule?.SOSPath;
        if (string.IsNullOrEmpty(nativeBinariesDirectory))
        {
            nativeBinariesDirectory = SOSPackageLayout.GetNativeBinariesDirectory();
        }
        return new HostAssetResolver(nativeBinariesDirectory);
    }
}
