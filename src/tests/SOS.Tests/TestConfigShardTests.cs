// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class TestConfigShardTests
{
    [Fact]
    public void ShardControlsCanBeUnset()
    {
        Assert.Null(ShardSelection.FromEnvironment(_ => null));
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData(null, "8")]
    public void ShardControlsMustBeSpecifiedTogether(string? index, string? count)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Parse(index, count));

        Assert.Contains("must either both be set or both be unset", error.Message);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("0", "-1")]
    [InlineData("0", " 8")]
    [InlineData("0", "eight")]
    public void ShardCountMustBeStrictlyPositive(string index, string count)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Parse(index, count));

        Assert.Contains("SOSHARNESS_SHARD_COUNT", error.Message);
    }

    [Theory]
    [InlineData("-1", "8")]
    [InlineData("8", "8")]
    [InlineData(" 0", "8")]
    [InlineData("zero", "8")]
    public void ShardIndexMustBeInRange(string index, string count)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Parse(index, count));

        Assert.Contains("SOSHARNESS_SHARD_INDEX", error.Message);
    }

    [Fact]
    public void CaptureFamilyExcludesReplayOnlyAxes()
    {
        TestConfig first = Config(Host.Cdb, Dac.Legacy);
        TestConfig second = Config(Host.DotnetDump, Dac.CDac);

        Assert.Equal(first.CaptureFamilyKey, second.CaptureFamilyKey);
        Assert.Equal(first.GetCaptureShard(8), second.GetCaptureShard(8));
    }

    [Fact]
    public void CaptureFamilyIncludesEveryCaptureAxis()
    {
        TestConfig baseline = Config(Host.Cdb, Dac.Legacy);

        Assert.NotEqual(baseline.CaptureFamilyKey, (baseline with { Target = TargetCatalog.DivZero }).CaptureFamilyKey);
        Assert.NotEqual(baseline.CaptureFamilyKey, (baseline with { Flavor = Flavor.SingleFile }).CaptureFamilyKey);
        Assert.NotEqual(baseline.CaptureFamilyKey, (baseline with { CoreVersion = CoreVersion.Net11 }).CaptureFamilyKey);
        Assert.NotEqual(baseline.CaptureFamilyKey, (baseline with { GcType = GcType.Server }).CaptureFamilyKey);
        Assert.NotEqual(baseline.CaptureFamilyKey, (baseline with { DumpKind = DumpKind.Full }).CaptureFamilyKey);
        Assert.NotEqual(baseline.CaptureFamilyKey, (baseline with { Liveness = Liveness.Live }).CaptureFamilyKey);
    }

    [Fact]
    public void StableHashHasFixedValue()
    {
        Assert.Equal(
            0x1C7CF9C1B972727AUL,
            TestConfig.StableHash("scenarios|Core|Net10|Workstation|Heap|Dump"));
        Assert.Equal(2, Config(Host.Cdb, Dac.Legacy).GetCaptureShard(8));
    }

    [Fact]
    public void ShardingUsesTransformedCaptureFamily()
    {
        TestConfig transformed = Config(Host.Cdb, Dac.Legacy) with { DumpKind = DumpKind.Full };
        ShardSelection shard = new(transformed.GetCaptureShard(8), 8);

        Assert.Equal(
            [transformed],
            TestConfig.ApplyShardFilter([transformed], shard).ToArray());
    }

    [Fact]
    public void MatrixPartitionControlsAllowEmptyTheories()
    {
        Assert.False(TestConfig.AllowEmptyMatrix(_ => null));
        Assert.True(TestConfig.AllowEmptyMatrix(name =>
            name == "SOSHARNESS_ONLY_LIVENESS" ? "Live" : null));
        Assert.True(TestConfig.AllowEmptyMatrix(name => name switch
        {
            "SOSHARNESS_SHARD_INDEX" => "31",
            "SOSHARNESS_SHARD_COUNT" => "32",
            _ => null,
        }));
    }

    private static ShardSelection? Parse(string? index, string? count) =>
        ShardSelection.FromEnvironment(name => name switch
        {
            "SOSHARNESS_SHARD_INDEX" => index,
            "SOSHARNESS_SHARD_COUNT" => count,
            _ => null,
        });

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
