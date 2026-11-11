// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// The "cdb" host over a snapshot/crash dump: hosts dbgeng in-process and opens the dump.
/// SOS output is captured through dbgeng's output callbacks rather than scraping stdout.
/// </summary>
public sealed class DbgEngDumpHost : DbgEngHostBase
{
    private readonly string _dumpPath;

    public override string Name => "cdb";

    public DbgEngDumpHost(string dumpPath)
    {
        _dumpPath = dumpPath;
        Initialize();
    }

    protected override void OnOpen()
    {
        int hr = Client.OpenDumpFile(_dumpPath);
        if (hr < 0)
        {
            throw new InvalidOperationException($"OpenDumpFile('{_dumpPath}') failed: 0x{hr:x8}");
        }

        Control.WaitForEvent(TimeSpan.FromSeconds(60));
    }
}
