// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Raw-memory and state-decoding commands: the memory dumpers (<c>!dp</c>/<c>!dd</c>/<c>!db</c> and the
/// <c>d</c>/<c>da</c>/<c>dc</c>/<c>dq</c>/<c>du</c>/<c>dw</c> family), <c>!threadstate</c>, <c>!taskstate</c>,
/// and <c>!dumpexceptions</c>. The memory dumpers are read against <c>FieldMarker</c>, whose field values are
/// known, so the bytes/words in the dump are verifiable.
/// </summary>
public sealed class MemoryAndDecodeTests
{
    public static TheoryData<TestConfig> ScenariosMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);
    public static TheoryData<TestConfig> NestedExceptionMatrix => TestConfig.BuildMatrix([TargetCatalog.NestedException]);
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task MemoryDumpers_ShowKnownFieldBytes(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // The memory dumpers are dotnet-dump REPL commands (cdb uses the native d*/dp/db). FieldMarker's
        // LongField is a known 64-bit value, so it appears verbatim in the pointer/qword dumps.
        ulong marker = target.FindUniqueObject("FieldMarker");
        ulong longValue = (ulong)TestTargets.SosHarnessScenarios.FieldMarkerLong;
        string longHex = longValue.ToString("x");

        string pointerDump = target.Sos($"dp {marker:x}").Text;
        if (IntPtr.Size == 4)
        {
            Assert.Contains(unchecked((uint)longValue).ToString("x8"), pointerDump, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(unchecked((uint)(longValue >> 32)).ToString("x8"), pointerDump, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(longHex, pointerDump, StringComparison.OrdinalIgnoreCase);
        }

        string qwordDump = target.Sos($"dq {marker:x}").Text;
        if (IntPtr.Size == 4)
        {
            Assert.Contains(unchecked((uint)longValue).ToString("x8"), qwordDump, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(unchecked((uint)(longValue >> 32)).ToString("x8"), qwordDump, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(longHex, qwordDump, StringComparison.OrdinalIgnoreCase);
        }

        target.Sos($"db {marker:x}").AssertContains(":"); // byte dump prints "<addr>: <bytes>"
    }

    [SosTheory]
    [MemberData(nameof(ScenariosMatrix))]
    public async Task ThreadState_DecodesStateFlags(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // Take a real thread-state value from clrthreads and decode it.
        Match state = Regex.Match(target.Sos("clrthreads").Text, @"\b([0-9a-fA-F]{6,8})\s+(?:Preemptive|Cooperative)");
        Assert.True(state.Success, "expected a thread state value from clrthreads");

        // Every thread in our test apps has at least one ThreadState bit set (e.g. TS_FullyInitialized,
        // and TS_Background on the finalizer/threadpool threads), so the state is never 0. A 0 here means
        // the DAC stopped populating DacpThreadData::state.
        uint stateValue = uint.Parse(state.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
        Assert.NotEqual(0u, stateValue);

        SosOutput decoded = target.Sos($"threadstate {state.Groups[1].Value}");
        Assert.NotEmpty(decoded.Text.Trim());
        Assert.DoesNotContain("Unrecognized", decoded.Text, StringComparison.Ordinal);
    }

    [SosTheory]
    [MemberData(nameof(DotnetDumpMatrix))]
    public async Task TaskState_DecodesTaskStatus(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // taskstate is a managed extension command (dotnet-dump only). The async gate's Task<int> is awaited
        // and never completed, so its status is WaitingForActivation.
        ulong task = target.FirstObjectOfExactType("System.Threading.Tasks.Task<System.Int32>");
        target.Sos($"taskstate {task:x}").AssertContains("WaitingForActivation");
    }

    [SosTheory]
    [MemberData(nameof(NestedExceptionMatrix))]
    public async Task DumpExceptions_ListsManagedExceptions(TestConfig config)
    {
        // A crash target's dump has the thrown exception(s) on the heap.
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosOutput exceptions = target.Sos("dumpexceptions");
        Assert.Contains("Exception", exceptions.Text, StringComparison.Ordinal);
    }
}
