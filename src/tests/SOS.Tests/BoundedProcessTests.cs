// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class BoundedProcessTests
{
    [Fact]
    public void DrainsLargeOutputConcurrently()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ProcessStartInfo startInfo = Shell(
            "i=0; while [ $i -lt 10000 ]; do echo stdout-$i; echo stderr-$i >&2; i=$((i+1)); done");

        BoundedProcessResult result = BoundedProcess.Run(startInfo, TimeSpan.FromSeconds(30));

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StandardOutput.Length > 64 * 1024);
        Assert.True(result.StandardError.Length > 64 * 1024);
    }

    [Fact]
    public void KillsLinuxProcessGroupOnTimeout()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ProcessStartInfo startInfo = Shell("sleep 30 & echo $!; wait");

        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeoutException error = Assert.Throws<TimeoutException>(
            () => BoundedProcess.Run(
                startInfo,
                TimeSpan.FromMilliseconds(250),
                isolateLinuxProcessGroup: true));
        stopwatch.Stop();

        string childPid = error.Message
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .First(line => int.TryParse(line, out _));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.False(IsRunning(childPid));
    }

    [Fact]
    public void ClosesInheritedOutputAfterParentExit()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ProcessStartInfo startInfo = Shell("sleep 30 & echo $!; exit 0");

        Stopwatch stopwatch = Stopwatch.StartNew();
        BoundedProcessResult result = BoundedProcess.Run(
            startInfo,
            TimeSpan.FromSeconds(10),
            isolateLinuxProcessGroup: true);
        stopwatch.Stop();

        string childPid = result.StandardOutput.Trim();
        Assert.Equal(0, result.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.False(IsRunning(childPid));
    }

    [Fact]
    public void WaitsForInheritedOutputWithinConfiguredDeadline()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        ProcessStartInfo startInfo = Shell("sleep 1 & echo inherited-output; exit 0");

        Stopwatch stopwatch = Stopwatch.StartNew();
        BoundedProcessResult result = BoundedProcess.Run(
            startInfo,
            TimeSpan.FromSeconds(2),
            outputDrainTimeout: TimeSpan.FromSeconds(3));
        stopwatch.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("inherited-output", result.StandardOutput);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(500));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    private static ProcessStartInfo Shell(string command)
    {
        ProcessStartInfo startInfo = new("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private static bool IsRunning(string processId)
    {
        string processPath = $"/proc/{processId}";
        string statPath = Path.Combine(processPath, "stat");
        string stat;
        try
        {
            stat = File.ReadAllText(statPath);
        }
        catch (IOException) when (!Directory.Exists(processPath))
        {
            return false;
        }

        int commandEnd = stat.LastIndexOf(')');
        return commandEnd < 0 || commandEnd + 2 >= stat.Length || stat[commandEnd + 2] != 'Z';
    }
}
