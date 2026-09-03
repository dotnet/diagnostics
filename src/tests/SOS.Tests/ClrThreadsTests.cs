// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// clrthreads across the full host × target × flavor matrix, with the assertion inline.
/// Demonstrates the cross-product MemberData shape and shared-dump reuse. This works uniformly
/// (including desktop .NET Framework under cdb) because the cdb host unloads any SOS cdb
/// auto-loaded and forces OUR modern SOS - which exposes <c>clrthreads</c> on every flavor.
/// (If we let cdb's auto-loaded desktop Framework SOS answer, it would only expose <c>threads</c>.)
/// </summary>
public sealed class ClrThreadsTests
{
    /// <summary>
    /// A matrix of all combinations of hosts, targets, and flavors.
    /// Hosts.DumpHosts = [cdb, dotnet-dump] || [lldb, dotnet-dump]
    /// Targets = debuggee targets to test
    /// Flavors = e.g. [Flavor.Core, Flavor.SingleFile, Flavor.Framework]
    /// </summary>
    public static TheoryData<TestConfig> Matrix { get; }
                    // Live opt-in: !clrthreads enumerates the live thread list, so this base test runs
                    // dump AND live as the representative live-thread-enumeration check.
                    = TestConfig.BuildMatrix(
                        [TargetCatalog.NestedException, TargetCatalog.Scenarios],
                        liveness: Liveness.AllValid,
                        dumpKind: DumpKind.All);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ClrThreads_ReportsThreadCount(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosOutput clrthreads = target.Sos("clrthreads");

        clrthreads["ThreadCount"].AssertValid(Sos.Dec);
        Assert.NotEqual(0u, clrthreads["ThreadCount"].AsUInt32(Sos.Dec));

        SosTable table = clrthreads.AsThreadsTable();
        Assert.NotEmpty(table);

        // The structured ThreadCount field equals the number of rows the table parsed (ties the
        // summary field to the per-thread rows).
        Assert.Equal(clrthreads["ThreadCount"].AsInt32(Sos.Dec), table.Length);

        // Every fixed column sliced into the right shape — proof the two-line "Lock Count" header and
        // the column alignment were handled. A misaligned column would put a non-address in ThreadOBJ,
        // or something other than the two GC modes / known apartment states in their columns.
        table.AssertAll(row => Sos.Addr.Matches(row["ThreadOBJ"]), "ThreadOBJ is an address");
        table.AssertAll(row => row["GC Mode"] == "Preemptive" || row["GC Mode"] == "Cooperative", "GC Mode is Preemptive or Cooperative");
        table.AssertAll(row => row["Apt"].Value is "MTA" or "STA" or "NTA" or "Ukn", "Apt is a known apartment state");

        // The trailing free-form Exception column parses: every managed process has a finalizer thread,
        // tagged "(Finalizer)" there.
        table.AssertContainsRow(row => row["Exception"].Contains("Finalizer"), "the (Finalizer) thread is tagged in the Exception column");
    }
}
