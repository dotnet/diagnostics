// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class LldbHostingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoProbingRetriesNet10WithNet11(bool architectureSpecificRoot)
    {
        RequireMacOS();
        string directory = CreateTestDirectory();
        try
        {
            AddFailingRuntime(directory, "10.0.2");
            AddFailingRuntime(directory, "10.0.1");
            string runtime = AddWorkingRuntime(directory);

            (int exitCode, string output) = await RunLldb(
                directory, architectureSpecificRoot, null, "setsymbolserver -disable", "sethostruntime");

            Assert.True(exitCode == 0, output);
            Assert.Contains("SOS test host: PAL_MachExceptionMode='7'", output);
            Assert.Contains(Path.Combine("10.0.2", "libcoreclr.dylib") + "' 8007000c", output);
            Assert.DoesNotContain("10.0.1/libcoreclr.dylib", output);
            Assert.Contains($"Host runtime path: {runtime}", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, "7")]
    [InlineData("7", "7")]
    [InlineData("0", "0")]
    [InlineData("", "")]
    public async Task ExplicitRuntimeAndMachExceptionModeArePreserved(string? mode, string expectedMode)
    {
        RequireMacOS();
        string directory = CreateTestDirectory();
        try
        {
            string runtime = AddFailingRuntime(directory, "10.0.1");
            AddWorkingRuntime(directory);

            (int exitCode, string output) = await RunLldb(
                directory, false, mode, $"sethostruntime '{runtime}'", "setsymbolserver -disable");

            Assert.True(exitCode != 0, output);
            Assert.Contains($"SOS test host: PAL_MachExceptionMode='{expectedMode}'", output);
            Assert.Contains(Path.Combine("10.0.1", "libcoreclr.dylib") + "' 8007000c", output);
            Assert.DoesNotContain("11.0.0/libcoreclr.dylib", output);
            Assert.DoesNotContain("Current symbol store settings:", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FailedNet11RetryDoesNotTryOtherRuntimes()
    {
        RequireMacOS();
        string directory = CreateTestDirectory();
        try
        {
            AddFailingRuntime(directory, "10.0.2");
            AddFailingRuntime(directory, "10.0.1");
            AddFailingRuntime(directory, "11.0.1");
            AddFailingRuntime(directory, "11.0.0");
            AddFailingRuntime(directory, "12.0.0");

            (int exitCode, string output) = await RunLldb(directory, false, null, "setsymbolserver -disable");

            Assert.True(exitCode != 0, output);
            Assert.Contains(Path.Combine("10.0.2", "libcoreclr.dylib") + "' 8007000c", output);
            Assert.Contains(Path.Combine("11.0.1", "libcoreclr.dylib") + "' 8007000c", output);
            Assert.DoesNotContain("10.0.1/libcoreclr.dylib", output);
            Assert.DoesNotContain("11.0.0/libcoreclr.dylib", output);
            Assert.DoesNotContain("12.0.0/libcoreclr.dylib", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("10.0.1")]
    [InlineData("11.0.1")]
    [InlineData("9.0.1")]
    [InlineData("8.0.1")]
    public async Task RetryRequiresNet10FailureAndInstalledNet11(string version)
    {
        RequireMacOS();
        string directory = CreateTestDirectory();
        try
        {
            AddFailingRuntime(directory, version);
            AddFailingRuntime(directory, "12.0.0");

            (int exitCode, string output) = await RunLldb(directory, false, null, "setsymbolserver -disable");

            Assert.True(exitCode != 0, output);
            Assert.Contains(Path.Combine(version, "libcoreclr.dylib") + "' 8007000c", output);
            Assert.DoesNotContain("12.0.0/libcoreclr.dylib", output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void RequireMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("Apple LLDB hosting is macOS-only.");
        }
    }

    private static string CreateTestDirectory()
    {
        string directory = Path.Combine(RepoLayout.Scratch, nameof(LldbHostingTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "home"));
        return directory;
    }

    private static string AddFailingRuntime(string directory, string version)
    {
        string fixture = Path.Combine(
            RepoLayout.IsHelix ? RepoLayout.DebuggerRoot : RepoLayout.ArtifactsBinNative, "libSOSHostRuntime.dylib");
        Assert.True(File.Exists(fixture), $"Prebuilt hosting test runtime was not found: {fixture}");
        string runtime = Path.Combine(directory, "shared", "Microsoft.NETCore.App", version);
        Directory.CreateDirectory(runtime);
        File.Copy(fixture, Path.Combine(runtime, "libcoreclr.dylib"));
        return runtime;
    }

    private static string AddWorkingRuntime(string directory)
    {
        string hostRuntime = Directory.GetDirectories(
            Path.Combine(RepoLayout.DotnetTestRoot, "shared", "Microsoft.NETCore.App"), "11.0.*")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .First();
        string runtime = Path.Combine(directory, "shared", "Microsoft.NETCore.App", "11.0.0");
        Directory.CreateDirectory(runtime);
        foreach (string file in Directory.EnumerateFiles(hostRuntime))
        {
            File.CreateSymbolicLink(Path.Combine(runtime, Path.GetFileName(file)), file);
        }
        return runtime;
    }

    private static async Task<(int ExitCode, string Output)> RunLldb(
        string directory, bool architectureSpecificRoot, string? mode, params string[] commands)
    {
        // Use Apple's executable, not the harness driver, to exercise its guarded exception ports.
        ProcessStartInfo start = new("/usr/bin/lldb")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--batch");
        start.ArgumentList.Add("--no-lldbinit");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add($"plugin load '{ToolPaths.LldbPluginPath}'");
        foreach (string command in commands)
        {
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(command);
        }
        start.Environment["HOME"] = Path.Combine(directory, "home");
        start.Environment.Remove("_NT_SYMBOL_PATH");
        start.Environment.Remove("PAL_MachExceptionMode");
        if (mode is not null)
        {
            start.Environment["PAL_MachExceptionMode"] = mode;
        }
        string rootVariable = "DOTNET_ROOT_" + RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
        start.Environment.Remove(rootVariable);
        start.Environment["DOTNET_ROOT"] = architectureSpecificRoot ? Path.Combine(directory, "unused-root") : directory;
        if (architectureSpecificRoot)
        {
            start.Environment[rootVariable] = directory;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Apple LLDB.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        return (process.ExitCode, await stdout + Environment.NewLine + await stderr);
    }
}
