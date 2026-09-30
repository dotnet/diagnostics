// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

internal static class SOSTestSkips
{
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
        string? reason = GetICorDebugFrameCountSkipReason(config, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture);
        if (reason is not null)
            Assert.Skip(reason);
    }

    internal static string? GetICorDebugFrameCountSkipReason(TestConfig config, bool isWindows, bool isMacOS, Architecture architecture)
    {
        if (config.Liveness != Liveness.Dump || config.DumpKind != DumpKind.Heap || config.GcType != GcType.Workstation)
            return null;

        // https://dev.azure.com/dnceng-public/public/_build/results?buildId=1616319
        // Four .NET 10 x86 NestedException failures: Core/SingleFile under CDB/dotnet-dump, legacy DAC.
        // Core returns two native frames; SingleFile returns too few frames. Both fail before any -c call.
        // Reproduced locally on .NET 10.0.12 (CI: 10.0.10): a Core Heap dump fails ReadVirtual for 28 bytes;
        // a Full dump recovers the managed frames. The missing structure and SingleFile cause remain unknown.
        // Equivalent Heap cases pass on .NET 8, 9, and 11 (including both DACs on 11).
        // Remove when the .NET 10 Heap cases pass; do not substitute Full dumps to hide the failure.
        if (isWindows
            && architecture == Architecture.X86
            && config.Target == TargetCatalog.NestedException
            && config.CoreVersion == CoreVersion.Net10
            && config.Flavor is Flavor.Core or Flavor.SingleFile
            && config.Host is Host.Cdb or Host.DotnetDump
            && config.Dac == Dac.Legacy)
        {
            return ".NET 10 Windows x86 ICorDebug cannot recover managed frames from NestedException Heap dumps.";
        }

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
