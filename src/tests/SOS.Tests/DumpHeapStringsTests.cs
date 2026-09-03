// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!dumpheap -strings</c> (and <c>-strings -stat</c>). The summary it prints is a
/// value report — each distinct string with its count/size — so this is a content check, not a shape
/// check: the <see cref="TargetCatalog.NestedException"/> debuggee's two exception message constants
/// (<c>InnerMessage</c>/<c>OuterMessage</c>, mirrored from the debuggee via source gen) must appear as
/// string values in the summary. <c>-strings -stat</c> drops the per-object listing.
/// </summary>
public sealed class DumpHeapStringsTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.NestedException]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpHeap_Strings(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        // -strings: a per-object listing of string objects, then the value summary.
        DumpHeapResult strings = target.DumpHeap("-strings");
        Assert.NotEmpty(strings.Objects);
        strings.Strings.AssertContainsRow(
            r => r["String"] == "Invalid operation exception, outer",
            $"the outer exception message \"{"Invalid operation exception, outer"}\"");
        strings.Strings.AssertContainsRow(
            r => r["String"] == "Bad format exception, inner",
            $"the inner exception message \"{"Bad format exception, inner"}\"");

        // -strings -stat: the value summary only, no per-object listing.
        DumpHeapResult statOnly = target.DumpHeap("-strings -stat");
        statOnly.Strings.AssertContainsRow(
            r => r["String"] == "Invalid operation exception, outer",
            $"the outer exception message \"{"Invalid operation exception, outer"}\"");
        Assert.Throws<SosAssertException>(() => statOnly.Objects);
    }
}
