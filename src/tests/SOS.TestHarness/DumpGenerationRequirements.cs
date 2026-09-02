// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace SOS.TestHarness;

/// <summary>
/// Handles the Windows machine prerequisite for capturing a reduced (Heap/Mini) .NET Core dump of an
/// <b>unsigned</b> test runtime.
///
/// <para><b>Why the prerequisite exists.</b> On Windows a reduced dump is written by <c>createdump</c> via
/// <c>MiniDumpWriteDump</c>, which only captures <c>MEM_PRIVATE</c> read/write pages directly. The CLR's
/// loader-allocator heaps — which hold the <c>MethodTable</c>/<c>Module</c> structures SOS and ClrMD need
/// to enumerate modules, types and the GC heap — are <c>MEM_MAPPED</c> (the double-mapped executable
/// allocator), so they are captured only through dbghelp's auxiliary DAC provider (dbghelp loads
/// <c>mscordaccore.dll</c> and calls <c>ICLRDataEnumMemoryRegions::EnumMemoryRegions</c>). dbghelp refuses
/// to load a DAC that isn't Authenticode-signed unless
/// <c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\MiniDumpSettings\DisableAuxProviderSignatureCheck</c>
/// is set to 1. The locally-built and preview (net11) test runtimes ship an <b>unsigned</b> DAC, so without
/// that value their loader heaps are silently omitted from a reduced dump and every module/type/heap
/// command then fails with "Unable to create a ClrHeap …". (Released runtimes such as net8–net10 have a
/// signed DAC; desktop Framework and single-file/Full captures do not use this path.)</para>
///
/// <para><b>Why we only check, never set.</b> That value lives under <c>HKLM</c>, so writing it requires
/// elevation and changes machine-wide state. When it is absent, Heap requests are captured as Full dumps,
/// which preserve the required data without changing the machine. Mini rows are skipped because substituting
/// a Full dump would not test Mini-dump behavior.</para>
/// </summary>
internal static class DumpGenerationRequirements
{
    private static readonly string s_root = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? @"SOFTWARE\WOW6432Node\" : @"SOFTWARE\";
    private static readonly string s_settingsNode = s_root + @"Microsoft\Windows NT\CurrentVersion\MiniDumpSettings";
    private const string DisableCheckValue = "DisableAuxProviderSignatureCheck";

    // Read the registry value at most once per process (cheap, read-only; reading HKLM needs no elevation).
    private static readonly Lazy<bool> s_signatureCheckDisabled = new(ReadSignatureCheckDisabled);

    /// <summary>
    /// Resolves the dump kind that can be captured on this machine. Only a <b>reduced</b> (Heap/Mini)
    /// <b>Core</b> dump goes
    /// through that path: Full dumps capture all memory directly, single-file snapshots are always collected
    /// Full, and desktop Framework is captured via dbgeng using the signed in-box DAC — none of those need
    /// the bypass, so their requested kind is returned unchanged (as it is on non-Windows).
    /// </summary>
    internal static DumpKind ResolveCaptureKind(Flavor flavor, DumpKind dumpKind)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return dumpKind;
        }

        if (dumpKind == DumpKind.Full || flavor == Flavor.Framework || flavor == Flavor.SingleFile)
        {
            return dumpKind;
        }

        if (s_signatureCheckDisabled.Value)
        {
            return dumpKind;
        }

        if (dumpKind == DumpKind.Mini)
        {
            HarnessSkipException.Now(
                $@"Mini dump capture requires HKLM\{s_settingsNode}\{DisableCheckValue}=1 so dbghelp can " +
                "load the unsigned test DAC");
        }

        return DumpKind.Full;
    }

    private static bool ReadSignatureCheckDisabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }

        return ReadSignatureCheckDisabledWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool ReadSignatureCheckDisabledWindows()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(s_settingsNode);
            return key?.GetValue(DisableCheckValue) is int value && value == 1;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
