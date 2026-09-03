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
    public static TheoryData<TestConfig> Matrix { get; }
        = TestMatrices.StackWalk(
            [
                TargetCatalog.DivZero,
                TargetCatalog.NestedException,
                TargetCatalog.LineNums,
                TargetCatalog.DynamicMethod,
            ]);

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
