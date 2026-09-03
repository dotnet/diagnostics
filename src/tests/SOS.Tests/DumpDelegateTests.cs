// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// <c>!dumpdelegate</c> on the debuggee's worker-thread delegate. Each parked worker is started from
/// <c>new Thread(WorkerEntry)</c>, so a <c>ThreadStart</c> delegate bound to
/// <c>SosHarnessScenarios.WorkerEntry()</c> is live on the heap; dumpdelegate must resolve its
/// target/method/name row to that method.
/// </summary>
public sealed class DumpDelegateTests
{
    public static TheoryData<TestConfig> Matrix
    {
        get
        {
            TheoryData<TestConfig> data = new();
            foreach (TestConfig config in TestConfig.Permutations([TargetCatalog.Scenarios]))
            {
                // .NET 11 single-file delegate method pointers are app-local stubs that the DAC cannot
                // map back to a MethodDesc, so dumpdelegate correctly has no resolvable method row.
                if (config.Flavor != Flavor.SingleFile || config.CoreVersion != CoreVersion.Net11)
                {
                    data.Add(config);
                }
            }

            return data;
        }
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpDelegate_ResolvesWorkerEntry(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // Find the ThreadStart delegate that targets WorkerEntry (workers are started with it).
        DelegateEntry? workerEntry = null;
        foreach (ulong candidate in target.DumpHeap("-type System.Threading.ThreadStart -short").ShortAddresses)
        {
            DumpDelegateResult dump = target.DumpDelegate(candidate);
            workerEntry = dump.Entries.FirstOrDefault(e => e.Name.Contains("WorkerEntry", StringComparison.Ordinal));
            if (workerEntry is { Name.Length: > 0 })
            {
                break;
            }
        }

        Assert.True(workerEntry is { Name.Length: > 0 }, "expected a ThreadStart delegate bound to WorkerEntry");
        DelegateEntry entry = workerEntry!.Value;
        Assert.Equal("SosHarnessScenarios.WorkerEntry()", entry.Name);
        Assert.NotEqual(0ul, entry.Method);
    }
}
