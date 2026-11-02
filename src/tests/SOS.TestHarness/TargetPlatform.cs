// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.TestHarness;

/// <summary>The platform that produced a debug target, independent of the debugger's platform.</summary>
public sealed record TargetPlatform
{
    public string Rid { get; }
    public Architecture Architecture { get; }
    public bool IsWindows => Rid.StartsWith("win-", StringComparison.Ordinal);
    public bool IsMacOS => Rid.StartsWith("osx-", StringComparison.Ordinal);
    public bool IsLinux => Rid.StartsWith("linux-", StringComparison.Ordinal);

    public TargetPlatform(string rid)
    {
        Rid = rid;
        if (!IsWindows && !IsMacOS && !IsLinux &&
            !rid.StartsWith("freebsd-", StringComparison.Ordinal) && !rid.StartsWith("netbsd-", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported dump platform '{rid}'.", nameof(rid));
        }

        Architecture = rid[(rid.LastIndexOf('-') + 1)..] switch
        {
            "x86" => Architecture.X86,
            "x64" => Architecture.X64,
            "arm" => Architecture.Arm,
            "arm64" => Architecture.Arm64,
            _ => throw new ArgumentException($"Unsupported dump architecture in '{rid}'.", nameof(rid)),
        };
    }

    internal bool Supports(TestConfig config, TargetPlatform analyzer) =>
        Rid == analyzer.Rid ||
        (!config.IsLive && config.Host == Host.DotnetDump && config.Dac == Dac.CDac &&
         config.Flavor != Flavor.Framework && TestConfig.IsDacSupported(config));
}
