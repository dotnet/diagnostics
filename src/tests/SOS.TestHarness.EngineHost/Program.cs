// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;

// The EngineHost child process: hosts dbgeng IN THIS PROCESS (reusing the in-process host
// classes) and serves commands over a stdin/stdout REPL. The test host spawns one of these per
// target and talks to it via EngineProtocol, so the test host itself never loads dbgeng/SOS/DAC —
// which is what was crashing it (ExecutionEngineException). If the engine corrupts THIS process,
// only this child dies and the test sees a clean failure.
//
// Usage:
//   EngineHost dump <dumpPath>
//   EngineHost live <exePath>

return EngineHostMain.Run(args);

internal static class EngineHostMain
{
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: EngineHost <dump <dumpPath> | live <exePath>>");
            return 2;
        }

        IDebuggerHost host;
        DbgEngLiveHost? liveHost = null;
        try
        {
            switch (args[0])
            {
                case "dump" when args.Length == 2:
                    host = new DbgEngDumpHost(args[1]);
                    host.LoadSos();
                    break;

                case "live" when args.Length == 2:
                    liveHost = new DbgEngLiveHost(args[1]); // launches, breaks, loads SOS
                    host = liveHost;
                    break;

                default:
                    Console.Error.WriteLine("invalid arguments");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("engine open failed: " + ex);
            return 1;
        }

        // Signal that the target is open and SOS is ready.
        Console.Out.WriteLine(EngineProtocol.Ready);
        Console.Out.Flush();

        try
        {
            string? line;
            while ((line = Console.In.ReadLine()) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                try
                {
                    SosOutput result;
                    if (line.StartsWith(EngineProtocol.RunToBpmdPrefix, StringComparison.Ordinal))
                    {
                        if (liveHost is null)
                        {
                            throw new InvalidOperationException("runtobpmd is only valid for a live engine.");
                        }

                        string[] parts = line[EngineProtocol.RunToBpmdPrefix.Length..].Split(' ', 2);
                        result = liveHost.RunToBpmd(parts[0], parts[1]);
                    }
                    else if (line == EngineProtocol.RunToCrash)
                    {
                        if (liveHost is null)
                        {
                            throw new InvalidOperationException("runtocrash is only valid for a live engine.");
                        }

                        result = liveHost.RunToCrash();
                    }
                    else if (line == EngineProtocol.RunToBreak)
                    {
                        if (liveHost is null)
                        {
                            throw new InvalidOperationException("runtobreak is only valid for a live engine.");
                        }

                        result = liveHost.RunToBreakpoint();
                    }
                    else
                    {
                        result = host.Execute(line);
                    }

                    Console.Out.Write(result.Text);
                    if (!result.Text.EndsWith('\n'))
                    {
                        Console.Out.Write('\n');
                    }

                    Console.Out.WriteLine(EngineProtocol.End);
                    Console.Out.Flush();
                }
                catch (Exception ex)
                {
                    Console.Out.WriteLine(ex.Message);
                    Console.Out.WriteLine(EngineProtocol.Error);
                    Console.Out.Flush();
                }
            }
        }
        finally
        {
            host.Dispose();
        }

        return 0;
    }
}
