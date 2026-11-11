// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.Minidump;
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
    public static TheoryData<TestConfig> NestedExceptionMatrix => TestMatrices.HeapEnumeration([TargetCatalog.NestedException]);
    public static TheoryData<TestConfig> DotnetDumpMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], Flavor.AllValid, Host.DotnetDump);
    public static TheoryData<TestConfig> MiniDumpMatrix => TestConfig.BuildMatrix(
        [TargetCatalog.Scenarios],
        Flavor.Core,
        Host.AllValid,
        Liveness.Dump,
        dumpKind: DumpKind.Mini);

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
    [MemberData(nameof(MiniDumpMatrix))]
    public async Task MemoryDumper_MapsOmittedManagedModuleData(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ClrModuleInfo coreLib = target.ClrModules().SingleByName("System.Private.CoreLib.dll");
        ulong imageAddress = FindOmittedImageAddress(target.DumpPath, coreLib.ImageBase, coreLib.ImageSize);

        if (config.Host == Host.Lldb)
        {
            SosOutput nativeRead = target.Execute($"memory read --size 1 --count 16 0x{imageAddress:x}");
            Assert.Contains("core file does not contain", nativeRead.Text, StringComparison.OrdinalIgnoreCase);
        }

        SosOutput mappedRead = target.Sos($"db {imageAddress:x}");
        Assert.Matches(
            $@"(?im)^{imageAddress:x16}:(?: [0-9a-f]{{2}}){{16}}",
            mappedRead.Text);
    }

    private static ulong FindOmittedImageAddress(string dumpPath, ulong imageBase, ulong imageSize)
    {
        const ulong ReadSize = 16;

        using StreamAddressSpace dataSource = new(File.OpenRead(dumpPath));
        (ulong Start, ulong End)[] savedRanges;

        if (OperatingSystem.IsWindows())
        {
            Minidump dump = new(dataSource);
            savedRanges = dump.Segments
                .Select(segment => (segment.VirtualAddress, segment.VirtualAddress + segment.Size))
                .ToArray();
        }
        else if (!OperatingSystem.IsMacOS())
        {
            ELFCoreFile dump = new(dataSource);
            Assert.True(dump.IsValid(), $"'{dumpPath}' is not an ELF core dump");
            savedRanges = dump.Segments
                .Where(segment => segment.Header.Type == ELFProgramHeaderType.Load && segment.Header.FileSize > 0)
                .Select(segment => (segment.Header.VirtualAddress.Value, segment.Header.VirtualAddress + segment.Header.FileSize))
                .ToArray();
        }
        else
        {
            MachOFile dump = new(dataSource);
            Assert.True(dump.IsValid() && dump.Header.FileType == MachHeaderFileType.Core, $"'{dumpPath}' is not a Mach-O core dump");
            savedRanges = dump.Segments
                .Where(segment => segment.LoadCommand.FileSize > 0)
                .Select(segment => ((ulong)segment.LoadCommand.VMAddress, segment.LoadCommand.VMAddress + segment.LoadCommand.FileSize))
                .ToArray();
        }

        ulong imageEnd = imageBase + imageSize;
        ulong address = imageBase;
        foreach ((ulong start, ulong end) in savedRanges.OrderBy(range => range.Start))
        {
            if (end <= address)
            {
                continue;
            }
            if (start >= imageEnd)
            {
                break;
            }
            if (address + ReadSize <= start)
            {
                return address;
            }
            address = Math.Max(address, end);
        }
        if (address + ReadSize <= imageEnd)
        {
            return address;
        }

        throw new InvalidOperationException(
            $"No omitted {ReadSize}-byte range was found in the CoreLib image.");
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
