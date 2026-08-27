// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SOS.TestHarness;

internal sealed record BoundedProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static partial class BoundedProcess
{
    private const int KillSignal = 9;
    private static readonly TimeSpan s_terminationTimeout = TimeSpan.FromSeconds(5);

    public static BoundedProcessResult Run(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        bool isolateLinuxProcessGroup = false)
    {
        if (!startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError)
        {
            throw new ArgumentException("Standard output and standard error must both be redirected.", nameof(startInfo));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        string command = $"{startInfo.FileName} {string.Join(' ', startInfo.ArgumentList)}".Trim();
        bool hasLinuxProcessGroup = isolateLinuxProcessGroup && OperatingSystem.IsLinux();
        if (hasLinuxProcessGroup)
        {
            WrapWithSetSid(startInfo);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{command}'.");
        int processGroupId = process.Id;
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeoutMilliseconds(timeout)))
        {
            Terminate(process, hasLinuxProcessGroup, processGroupId);
            BoundedProcessResult output = DrainOutput(process, stdoutTask, stderrTask, command);
            throw new TimeoutException(
                $"'{command}' did not exit within {timeout}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{output.StandardOutput}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{output.StandardError}");
        }

        // Linux single-file dump helpers can survive the target while retaining its redirected handles.
        // End the isolated group before waiting for stream EOF so those descendants cannot wedge drainage.
        if (hasLinuxProcessGroup)
        {
            KillProcessGroup(processGroupId);
        }

        return DrainOutput(process, stdoutTask, stderrTask, command);
    }

    private static BoundedProcessResult DrainOutput(
        Process process,
        Task<string> stdoutTask,
        Task<string> stderrTask,
        string command)
    {
        Task outputTask = Task.WhenAll(stdoutTask, stderrTask);
        if (!outputTask.Wait(s_terminationTimeout))
        {
            throw new TimeoutException(
                $"'{command}' exited with code {process.ExitCode}, but its redirected output did not close " +
                $"within {s_terminationTimeout}.");
        }

        return new BoundedProcessResult(
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
    }

    private static void Terminate(Process process, bool hasLinuxProcessGroup, int processGroupId)
    {
        if (hasLinuxProcessGroup)
        {
            KillProcessGroup(processGroupId);
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.WaitForExit(TimeoutMilliseconds(s_terminationTimeout));
    }

    private static void WrapWithSetSid(ProcessStartInfo startInfo)
    {
        string setSid = File.Exists("/usr/bin/setsid") ? "/usr/bin/setsid" :
            File.Exists("/bin/setsid") ? "/bin/setsid" :
            throw new FileNotFoundException("Could not locate setsid for Linux process-group isolation.");

        string executable = startInfo.FileName;
        string[] arguments = startInfo.ArgumentList.ToArray();
        startInfo.FileName = setSid;
        startInfo.ArgumentList.Clear();
        startInfo.ArgumentList.Add(executable);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static void KillProcessGroup(int processGroupId)
    {
        _ = KillUnix(-processGroupId, KillSignal);
    }

    private static int TimeoutMilliseconds(TimeSpan timeout) =>
        (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int KillUnix(int processId, int signal);
}
