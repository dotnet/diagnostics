// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class TestConfigValidityTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    public void SingleFileSnapshotExclusionIsStrict(string? value, bool expected)
    {
        Assert.Equal(expected, TestConfig.ExcludeSingleFileSnapshots(value));
    }

    [Theory]
    [InlineData("true")]
    [InlineData(" 1")]
    [InlineData("yes")]
    public void SingleFileSnapshotExclusionRejectsInvalidValues(string value)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => TestConfig.ExcludeSingleFileSnapshots(value));

        Assert.Contains("SOSHARNESS_EXCLUDE_SINGLEFILE_SNAPSHOTS", error.Message);
    }

    [Fact]
    public void CDacRequiresSupportedCoreConfiguration()
    {
        TestConfig config = Config() with { Dac = Dac.CDac, CoreVersion = CoreVersion.Net11 };

        Assert.True(TestConfig.IsDacSupported(config));
        Assert.False(TestConfig.IsDacSupported(config with { CoreVersion = CoreVersion.Net10 }));
        Assert.False(TestConfig.IsDacSupported(config with { Flavor = Flavor.Framework }));
        Assert.False(TestConfig.IsDacSupported(config with { Flavor = Flavor.SingleFile }));
        Assert.False(TestConfig.IsValid(config with { Flavor = Flavor.SingleFile }));
    }

    [Theory]
    [InlineData(Flavor.Core, "linux-musl-x64", true)]
    [InlineData(Flavor.SingleFile, "linux-x64", true)]
    [InlineData(Flavor.SingleFile, "linux-musl-x64", false)]
    [InlineData(Flavor.SingleFile, "linux-musl-arm64", false)]
    public void MuslExcludesOnlySingleFile(Flavor flavor, string rid, bool expected)
    {
        Assert.Equal(expected, TestConfig.IsFlavorSupportedOnRid(flavor, rid));
    }

    [Fact]
    public void Net8LinuxArm64CreatedumpPermissionFailureIsKnown()
    {
        const string error = "open(/proc/123/mem) FAILED Permission denied (13)";

        Assert.True(SnapshotStore.IsKnownCreatedumpPermissionFailure(
            CoreVersion.Net8, Architecture.Arm64, isLinux: true, error, string.Empty));
        Assert.False(SnapshotStore.IsKnownCreatedumpPermissionFailure(
            CoreVersion.Net11, Architecture.Arm64, isLinux: true, error, string.Empty));
        Assert.False(SnapshotStore.IsKnownCreatedumpPermissionFailure(
            CoreVersion.Net8, Architecture.X64, isLinux: true, error, string.Empty));
        Assert.False(SnapshotStore.IsKnownCreatedumpPermissionFailure(
            CoreVersion.Net8, Architecture.Arm64, isLinux: true, "unrelated failure", string.Empty));
    }

    [Theory]
    [InlineData(false, RegistryView.Registry32)]
    [InlineData(true, RegistryView.Registry64)]
    [SupportedOSPlatform("windows")]
    public void DumpGenerationRegistryViewMatchesProcessBitness(bool is64BitProcess, RegistryView expected)
    {
        Assert.Equal(expected, DumpGenerationRequirements.RegistryViewForProcess(is64BitProcess));
    }

    private static TestConfig Config() =>
        new(
            TargetCatalog.DivZero,
            OperatingSystem.IsWindows() ? Host.Cdb : Host.Lldb,
            Flavor.Core,
            Liveness.Dump,
            GcType.Workstation,
            DumpKind.Heap,
            CoreVersion.Net10,
            Dac.Legacy);
}
