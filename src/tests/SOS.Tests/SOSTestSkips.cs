// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

internal static class SOSTestSkips
{
    internal static void SkipFindRootsNotifications(TestConfig config)
    {
        Assert.SkipWhen(
            config.Host == Host.Lldb && config.CoreVersion == CoreVersion.Net10,
            "https://github.com/dotnet/runtime/pull/120083: .NET 10 GC notifications require target-memory allocation, " +
            "which the LLDB host does not support. .NET 11 uses a preallocated notification bitmask.");
    }

    internal static void SkipUnsupportedDictionary(TestConfig config)
    {
        Assert.SkipWhen(
            !OperatingSystem.IsWindows() && config.CoreVersion == CoreVersion.Net10,
            "https://github.com/dotnet/diagnostics/issues/5491: concurrent dictionary data is unavailable in .NET 10 Unix dumps.");
    }

    internal static void SkipDumpGcDataPerHeapStatistics(TestConfig config)
    {
        Assert.SkipWhen(
            config.CoreVersion is CoreVersion.Net8 or CoreVersion.Net9,
            "https://github.com/dotnet/runtime/pull/119393: the dumpgcdata per-heap statistics regression was fixed in .NET 10.");
    }

    internal static void SkipDumpLog(TestConfig config)
    {
        Assert.SkipWhen(
            config.Flavor == Flavor.Framework && config.Liveness == Liveness.Dump,
            "Desktop CLR DumpLog cannot read the stress-log header from a heap dump. " +
            "See https://github.com/dotnet/diagnostics/issues/5757.");
    }

    internal static void SkipStackOverflowDump(TestConfig config)
    {
        if (config.Liveness != Liveness.Dump)
            return;

        Assert.SkipWhen(
            config.Flavor == Flavor.SingleFile,
            "SingleFile dumps do not expose the stack overflow exception information. " +
            "See https://github.com/dotnet/diagnostics/issues/2515.");
        Assert.SkipWhen(
            config.Flavor == Flavor.Framework,
            "Desktop CLR dumps do not expose the stack overflow exception through the thread data. " +
            "See https://github.com/dotnet/diagnostics/issues/5757.");
    }

    internal static void SkipICorDebugStackWalk(TestConfig config)
    {
        Skip(GetX86DebugInfoSkipReason(config, OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture));
    }

    internal static void SkipFaultingExceptionFrame()
    {
        // The legacy exception-frame script did not run on ARM/ARM64 in CI.
        // Integer division by zero uses a software throw on these architectures, not a hardware fault.
        // ARM64 stacks show HelperMethodFrame on .NET 8/9 and SoftwareExceptionFrame on .NET 10/11.
        // https://dev.azure.com/dnceng-public/public/_build/results?buildId=1616829
        Assert.SkipWhen(
            RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64,
            "DivZero does not produce a FaultingExceptionFrame on ARM/ARM64; the legacy test did not run on these platforms.");
    }

    internal static void SkipICorDebugFrameCount(TestConfig config)
    {
        Skip(GetICorDebugFrameCountSkipReason(config, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture));
    }

    private static void Skip(string? reason)
    {
        if (reason is not null)
            Assert.Skip(reason);
    }

    /// <summary>
    /// Every ICorDebug stack walk decodes each frame's compressed debug info, so all of these tests share
    /// the same Windows x86 legacy DAC defect.
    /// </summary>
    internal static string? GetX86DebugInfoSkipReason(TestConfig config, bool isWindows, Architecture architecture)
    {
        // https://github.com/dotnet/runtime/issues/136331
        // In coreclr's debuginfostore.cpp, EnumMemoryRegions pads debug info to NibbleChunkType (4 bytes
        // on x86), but DoBounds/ReadFromBitOffsets uses aligned 8-byte loads. The last load can extend
        // up to 4 bytes past the enumerated range in reduced dumps and truncate an ICorDebug walk.
        bool affectedRuntime = config.CoreVersion is CoreVersion.Net10 or CoreVersion.Net11;
        if (isWindows
            && architecture == Architecture.X86
            && config.Liveness == Liveness.Dump
            && config.DumpKind != DumpKind.Full
            && affectedRuntime
            && config.Flavor is Flavor.Core or Flavor.SingleFile
            && config.Dac == Dac.Legacy)
        {
            return "https://github.com/dotnet/runtime/issues/136331: Windows x86 legacy DAC ICorDebug stack walks " +
                "can truncate when compressed debug info extends past the region enumerated into a reduced dump.";
        }

        return null;
    }

    internal static string? GetICorDebugFrameCountSkipReason(TestConfig config, bool isWindows, bool isMacOS, Architecture architecture)
    {
        string? x86Reason = GetX86DebugInfoSkipReason(config, isWindows, architecture);
        if (x86Reason is not null)
            return x86Reason;

        if (config.Liveness != Liveness.Dump || config.DumpKind != DumpKind.Heap || config.GcType != GcType.Workstation)
            return null;

        // https://dev.azure.com/dnceng-public/public/_build/results?buildId=1615278&view=ms.vss-test-web.build-test-results-tab&runId=44719894&resultId=100677
        // Helix job 957ba6e6-3fa2-4b1d-ab8c-1db7f5fd7a26, work item SOS_osx-arm64_Debug-Net11:
        // DNCHELIXMAC046, macOS 15.4.1, test-host runtime 11.0.0-rc.2.26465.113; all four targets below fail.
        // NestedException replay stdout: clrstack -i prints one NativeStackFrame then END_COMMAND_ERROR,
        // without an HRESULT or "Stack walk complete". Ordinary clrstack -all -r recovers managed frames.
        // SingleFile/legacy DAC and Core/both DACs pass for all four targets in the same work item.
        // Raw stdout, matching runtime build IDs, and dump archive are available via the Helix file list:
        // https://helix.dot.net/api/2019-06-17/jobs/957ba6e6-3fa2-4b1d-ab8c-1db7f5fd7a26/workitems/SOS_osx-arm64_Debug-Net11/files
        // Root cause remains open: compare cDAC/legacy in fresh processes on the same dump with matching
        // SingleFile executable and DAC/DBI, enable logging, and trace the first failing ICorDebug call/read.
        // Missing data is not proven here. The later build 1616319 hit HTTP 502 before macOS tests ran.
        // Bypass this rule locally to investigate; remove after all four Heap cases pass baseline, -c N,
        // and over-limit checks with the fixed binaries in CI. Keep the passing controls enabled.
        if (isMacOS
            && architecture == Architecture.Arm64
            && config.Target is TargetCatalog.DivZero or TargetCatalog.NestedException or TargetCatalog.LineNums or TargetCatalog.DynamicMethod
            && config.CoreVersion == CoreVersion.Net11
            && config.Flavor == Flavor.SingleFile
            && config.Host == Host.DotnetDump
            && config.Dac == Dac.CDac)
        {
            return ".NET 11 macOS ARM64 SingleFile cDAC ICorDebug stack walks fail before frame-limit checks. " +
                "The cDAC investigation remains open.";
        }

        return null;
    }
}
