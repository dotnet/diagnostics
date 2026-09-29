// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class ClrStackRuntimeFramesTests
{
    public static TheoryData<TestConfig> FaultingMatrix { get; } =
        TestMatrices.StackWalk([TargetCatalog.DivZero]);

    public static TheoryData<TestConfig> SoftwareMatrix { get; } =
        TestMatrices.StackWalk([TargetCatalog.SimpleThrow]);

    [SosTheory]
    [MemberData(nameof(FaultingMatrix))]
    public Task ClrStack_ReportsFaultingExceptionFrame(TestConfig config) =>
        AssertExceptionFrame(config, "FaultingExceptionFrame");

    [SosTheory]
    [MemberData(nameof(SoftwareMatrix))]
    public Task ClrStack_ReportsSoftwareExceptionFrame(TestConfig config)
    {
        Assert.SkipWhen(
            config.Flavor == Flavor.Framework || config.CoreVersion is CoreVersion.Net8 or CoreVersion.Net9,
            "SimpleThrow uses SoftwareExceptionFrame only on .NET 10 and later.");

        return AssertExceptionFrame(config, "SoftwareExceptionFrame");
    }

    private static async Task AssertExceptionFrame(TestConfig config, string frameName)
    {
        // Preserve the Windows x86 exclusion in both legacy exception-frame tests.
        Assert.SkipWhen(
            OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X86,
            "Exception-frame tests do not support x86 on Windows.");

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosTable stack = target.Clrstack();
        SosToken expected = new(frameName, $@"\[{frameName}: {Sos.Addr.Pattern}\]");
        stack.AssertContainsRow(
            row => row["InternalFrame"].AsBoolean() && expected.Matches(row["Call Site"]),
            $"a {frameName} row with a frame address");
    }
}
