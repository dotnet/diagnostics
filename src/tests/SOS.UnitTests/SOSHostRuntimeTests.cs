// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;

public class SOSHostRuntimeTests
{
    [Theory]
    [InlineData("clean")]
    [InlineData("conflict")]
    [InlineData("missing")]
    [InlineData("cleanup")]
    public Task FrameworkHostIsolatesDependenciesAndInitialization(string scenario) =>
        RunFrameworkHostTest(scenario);

    [Theory]
    [InlineData("native-update-failure")]
    [InlineData("native-registration-failure")]
    [InlineData("native-no-target")]
    [InlineData("native-success")]
    public Task NativeHostInitializationHandlesTargetStatus(string scenario)
    {
        if (OS.Kind != OSKind.Windows)
        {
            Assert.Skip("Native SOS hosting test is Windows-only");
        }

        TestConfiguration config = SOSTestHelpers.GetNetCoreConfigurations()
            .Select(arguments => (TestConfiguration)arguments[0])
            .First();
        return RunFrameworkHostTest(scenario, config.SOSPath());
    }

    private static async Task RunFrameworkHostTest(string scenario, string sosPath = null)
    {
        if (OS.Kind != OSKind.Windows)
        {
            Assert.Skip(".NET Framework hosting is Windows-only");
        }

        string hostDirectory = Path.Combine(AppContext.BaseDirectory, "FrameworkHostTest");
        string directory = Path.Combine(Path.GetTempPath(), "SOS.FrameworkHost-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ProcessStartInfo start = new(Path.Combine(hostDirectory, "SOS.FrameworkHost.TestHost.exe"))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(hostDirectory, "SOS.FrameworkHost.dll"));
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add(scenario);
            if (sosPath != null)
            {
                start.ArgumentList.Add(sosPath);
            }
            using Process process = Process.Start(start);
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(1));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
            Assert.True(process.ExitCode == 0, await output + Environment.NewLine + await error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
