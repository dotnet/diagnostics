// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!clrstack -c &lt;n&gt;</c> (limit the number of printed frames), as the legacy
/// ClrStackWithNumberOfFrames.script did with DivZero. Self-consistency oracle: <c>-c N</c> is exactly
/// the first N rows of the full <c>clrstack</c> (SOS counts every printed row toward the limit,
/// internal frames included), and N larger than the stack prints the whole stack without truncating.
/// Exercised over deep-stacked debuggees.
/// </summary>
public sealed class ClrStackFrameCountTests
{
    private static readonly string[] s_targets =
    [
        TargetCatalog.DivZero,
        TargetCatalog.NestedException,
        TargetCatalog.LineNums,
        TargetCatalog.DynamicMethod,
    ];

    public static TheoryData<TestConfig> Matrix { get; } = TestMatrices.StackWalk(s_targets);

    public static TheoryData<TestConfig> ICorDebugMatrix { get; } =
        TestMatrices.StackWalk(s_targets, filter: TestMatrices.SupportsICorDebugStackWalk);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ClrStack_FrameCount(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosTable full = target.Clrstack();
        Assert.True(full.Length >= 2, "expected a deep enough stack to exercise -c");

        // -c N is the first N rows of the full stack, for N within the stack...
        for (int n = 1; n <= full.Length; n++)
        {
            SosTable limited = target.ClrstackFrames(n);
            AssertSameFrames(full, limited, n);
        }

        // ...and N past the end prints the whole stack, no truncation, no padding.
        SosTable over = target.ClrstackFrames(full.Length + 5);
        AssertSameFrames(full, over, full.Length);
    }

    [SosTheory]
    [MemberData(nameof(ICorDebugMatrix))]
    public async Task ClrStack_ICorDebugFrameCount(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        IReadOnlyList<TargetExtensions.IcorFrame> full = target.ClrstackICorDebug(variables: false);
        Assert.True(full.Count >= 2, "expected a deep enough stack to exercise -i -c");
        Assert.Contains(full, frame => frame.IsManaged);

        for (int n = 1; n <= full.Count; n++)
        {
            IReadOnlyList<TargetExtensions.IcorFrame> limited = target.ClrstackICorDebug(variables: false, count: n);
            Assert.Equal(n, limited.Count);
            Assert.Equal(full.Take(n).Select(frame => frame.CallSite), limited.Select(frame => frame.CallSite));
        }

        IReadOnlyList<TargetExtensions.IcorFrame> over = target.ClrstackICorDebug(variables: false, count: full.Count + 5);
        Assert.Equal(full.Count, over.Count);
        Assert.Equal(full.Select(frame => frame.CallSite), over.Select(frame => frame.CallSite));
    }

    private static void AssertSameFrames(SosTable full, SosTable limited, int expectedCount)
    {
        Assert.Equal(expectedCount, limited.Length);
        for (int i = 0; i < expectedCount; i++)
        {
            Assert.Equal(full.Row(i)["Child SP"].Value, limited.Row(i)["Child SP"].Value);
            Assert.Equal(full.Row(i)["IP"].Value, limited.Row(i)["IP"].Value);
            Assert.Equal(full.Row(i)["Call Site"].Value, limited.Row(i)["Call Site"].Value);
        }
    }
}
