// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Entry point for the out-of-process capture helper (the <c>Capturer</c> exe). Captures the
/// desktop .NET Framework dumps for one <c>(target)</c> using in-process dbgeng — but in a
/// short-lived <em>child</em> process, so the heavy/risky live-debugging dbgeng work never runs
/// inside the test host (where it can corrupt the host CLR; see the design note's subprocess
/// recommendation). On success the dumps land in the given directory and the process exits 0.
/// </summary>
public static class CaptureCli
{
    /// <summary>Args: <c>&lt;exePath&gt; &lt;targetName&gt; &lt;dumpDir&gt; &lt;dumpKind&gt;</c>.</summary>
    public static int Run(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("usage: Capturer <debuggeeExe> <targetName> <dumpDir> <dumpKind>");
            return 2;
        }

        string exePath = args[0];
        string targetName = args[1];
        string dumpDir = args[2];
        if (!Enum.TryParse(args[3], ignoreCase: true, out DumpKind dumpKind))
        {
            Console.Error.WriteLine($"Unknown dump kind '{args[3]}'.");
            return 2;
        }

        try
        {
            TargetDefinition target = TargetCatalog.Get(targetName);
            Directory.CreateDirectory(dumpDir);
            DbgEngCapturer.Capture(exePath, target, dumpDir, dumpKind);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
