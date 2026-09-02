// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class TestMatrixCapabilityTests
{
    [Fact]
    public void HeapEnumerationExcludesWindowsSingleFileCDacDumps()
    {
        TestConfig config = Config(Host.Cdb, Dac.CDac) with
        {
            Flavor = Flavor.SingleFile,
            CoreVersion = CoreVersion.Net11,
        };

        Assert.False(TestMatrices.SupportsHeapEnumeration(config, isWindows: true));
        Assert.True(TestMatrices.SupportsHeapEnumeration(config, isWindows: false));
        Assert.False(TestMatrices.SupportsHeapEnumeration(config with { Host = Host.DotnetDump }, isWindows: true));
        Assert.True(TestMatrices.SupportsHeapEnumeration(config with { Dac = Dac.Legacy }, isWindows: true));
        Assert.True(TestMatrices.SupportsHeapEnumeration(config with { Flavor = Flavor.Core }, isWindows: true));
    }

    [Fact]
    public void GcRootEnumerationExcludesOnlyFramework()
    {
        TestConfig config = Config(Host.Cdb, Dac.Legacy);

        Assert.True(TestMatrices.SupportsGcRootEnumeration(config));
        Assert.False(TestMatrices.SupportsGcRootEnumeration(config with { Flavor = Flavor.Framework }));
    }

    [Fact]
    public void CurrentThreadCommandsExcludeOnlyLldbDumps()
    {
        TestConfig config = Config(Host.Lldb, Dac.Legacy);

        Assert.False(TestMatrices.SupportsCurrentThread(config));
        Assert.True(TestMatrices.SupportsCurrentThread(config with { Liveness = Liveness.Live }));
        Assert.True(TestMatrices.SupportsCurrentThread(config with { Host = Host.DotnetDump }));
        Assert.True(TestMatrices.SupportsCurrentThread(config with { Host = Host.Cdb }));
    }

    [Fact]
    public void ICorDebugStackWalkExcludesOnlyX64CdbFrameworkDivZeroDumps()
    {
        TestConfig config = Config(Host.Cdb, Dac.Legacy) with
        {
            Target = TargetCatalog.DivZero,
            Flavor = Flavor.Framework,
        };

        Assert.False(TestMatrices.SupportsICorDebugStackWalk(config, is64BitProcess: true));
        Assert.True(TestMatrices.SupportsICorDebugStackWalk(config, is64BitProcess: false));
        Assert.True(TestMatrices.SupportsICorDebugStackWalk(config with { Host = Host.DotnetDump }, is64BitProcess: true));
        Assert.True(TestMatrices.SupportsICorDebugStackWalk(config with { Target = TargetCatalog.Scenarios }, is64BitProcess: true));
        Assert.True(TestMatrices.SupportsICorDebugStackWalk(config with { Flavor = Flavor.Core }, is64BitProcess: true));
    }

    [Theory]
    [InlineData("Calculating live objects, this may take a while...", true)]
    [InlineData("Calculating live objects complete: 42 objects from 3 roots", true)]
    [InlineData("Caching GC roots, this may take a while.", true)]
    [InlineData("Subsequent runs of this command will be faster.", true)]
    [InlineData("0000000123456780", false)]
    public void NotReachableInRangeIgnoresOnlyLivenessProgress(string line, bool expected)
    {
        Assert.Equal(expected, NativeAddressSpaceTests.IsLivenessProgress(line));
    }

    private static TestConfig Config(Host host, Dac dac) =>
        new(
            TargetCatalog.Scenarios,
            host,
            Flavor.Core,
            Liveness.Dump,
            GcType.Workstation,
            DumpKind.Heap,
            CoreVersion.Net10,
            dac);
}
