// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!clrstack -all</c> (every managed thread's stack). The legacy scripts ran <c>-all</c>
/// on WebApp/DualRuntimes/FindRoots but only shape-checked it. The ManagedThreads target parks a fixed
/// number of worker threads at a known method (WorkerPark) via a barrier, so the enumeration is
/// deterministic: there must be exactly the expected number of workers parked in WorkerPark plus the
/// main thread (… AtAllThreads → Main). Self-consistency: the current thread's section in <c>-all</c>
/// matches plain <c>clrstack</c> (same frame IPs).
/// </summary>
public sealed class ClrStackAllThreadsTests
{
    private const int ExpectedWorkers = 3;

    public static TheoryData<TestConfig> Matrix { get; } = TestMatrices.StackWalk([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ClrStack_AllThreads(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopAllThreads);

        IReadOnlyList<TargetExtensions.ThreadStack> threads = target.ClrstackAllThreads();

        // Every worker thread is parked in WorkerPark; there are exactly ExpectedWorkers of them.
        int workers = threads.Count(t => t.Frames.Any(f => f.Function.Contains("SosHarnessScenarios.WorkerPark", StringComparison.Ordinal)));
        Assert.Equal(ExpectedWorkers, workers);

        // Exactly one thread is the main thread, in AtAllThreads below Main.
        TargetExtensions.ThreadStack main = Assert.Single(
            threads, t => t.Frames.Any(f => f.Function.Contains("SosHarnessScenarios.AtAllThreads", StringComparison.Ordinal)));
        Assert.Contains(main.Frames, f => f.Function.Contains("SosHarnessScenarios.Main", StringComparison.Ordinal));

        // -all enumerates at least the main thread plus the workers.
        Assert.True(threads.Count >= ExpectedWorkers + 1, $"expected >= {ExpectedWorkers + 1} threads, got {threads.Count}.");

        // Each OS thread id is distinct.
        Assert.Equal(threads.Count, threads.Select(t => t.OsThreadId).Distinct().Count());

        // Self-consistency: plain clrstack is the current (main) thread, so its frame IPs match the
        // main thread's section in -all.
        List<string> plainIps = target.Clrstack()
            .Select(r => r["IP"].Value.ToUpperInvariant())
            .ToList();
        List<string> mainIps = main.Frames
            .Where(f => f.IP.Length > 0)
            .Select(f => f.IP.ToUpperInvariant())
            .ToList();
        Assert.Equal(plainIps, mainIps);
    }
}
