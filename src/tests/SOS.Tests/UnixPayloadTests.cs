// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class UnixPayloadTests
{
    [Fact]
    public void DirectoryOverrideUsesConfiguredPath()
    {
        string expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sos-harness-scratch"));
        string defaultPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "default-scratch"));

        Assert.Equal(expected, RepoLayout.ResolveDirectory(expected, "/unused"));
        Assert.Equal(defaultPath, RepoLayout.ResolveDirectory(null, defaultPath));
    }

    [Fact]
    public void UploadRootRoutesHarnessArtifacts()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sos-repo"));
        string uploadRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sos-upload"));

        Assert.Equal(
            Path.Combine(uploadRoot, "failure-diagnostics", "crashdumps"),
            HostDiagnostics.ResolveCrashDumpDirectory(uploadRoot, repoRoot));
        Assert.Equal(
            Path.Combine(uploadRoot, "SOS-replays"),
            SosReplayAttribute.ResolveReplayDirectory(uploadRoot, repoRoot));
        Assert.Equal(
            Path.Combine(repoRoot, "artifacts", "replays", "crashdumps"),
            HostDiagnostics.ResolveCrashDumpDirectory(null, repoRoot));
        Assert.Equal(
            Path.Combine(repoRoot, "artifacts", "TestResults", "SOS.Tests"),
            SosReplayAttribute.ResolveReplayDirectory(null, repoRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetUsesWritableOverlay(bool sourceIsExecutable)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string testRoot = Path.Combine(Path.GetTempPath(), $"sos-payload-{Guid.NewGuid():N}");
        string sourceRoot = Path.Combine(testRoot, "payload");
        string sourceDirectory = Path.Combine(sourceRoot, "artifacts", "bin", "Debuggee");
        string overlayRoot = Path.Combine(testRoot, "overlay");
        string sourceExecutable = Path.Combine(sourceDirectory, "Debuggee");
        string sourceSidecar = Path.Combine(sourceDirectory, "Debuggee.dll");

        try
        {
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(sourceExecutable, "executable");
            File.WriteAllText(sourceSidecar, "sidecar");
            UnixFileMode sourceMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            if (sourceIsExecutable)
            {
                sourceMode |= UnixFileMode.UserExecute;
            }
            File.SetUnixFileMode(sourceExecutable, sourceMode);

            string executable = SnapshotStore.EnsureExecutable(sourceExecutable, overlayRoot, sourceRoot);

            Assert.NotEqual(sourceExecutable, executable);
            Assert.Equal("executable", File.ReadAllText(executable));
            Assert.Equal("sidecar", File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "Debuggee.dll")));
            Assert.True((File.GetUnixFileMode(executable) & UnixFileMode.UserExecute) != 0);
            Assert.Equal(
                sourceIsExecutable,
                (File.GetUnixFileMode(sourceExecutable) & UnixFileMode.UserExecute) != 0);

            File.SetLastWriteTimeUtc(executable, DateTime.UtcNow.AddDays(-1));
            DateTime overlayWriteTime = File.GetLastWriteTimeUtc(executable);

            Assert.Equal(executable, SnapshotStore.EnsureExecutable(sourceExecutable, overlayRoot, sourceRoot));
            Assert.Equal(overlayWriteTime, File.GetLastWriteTimeUtc(executable));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentExecutablePreparationPublishesCompleteExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string testRoot = Path.Combine(Path.GetTempPath(), $"sos-payload-{Guid.NewGuid():N}");
        string sourceRoot = Path.Combine(testRoot, "payload");
        string sourceDirectory = Path.Combine(sourceRoot, "artifacts", "bin", "Debuggee");
        string overlayRoot = Path.Combine(testRoot, "overlay");
        string sourceExecutable = Path.Combine(sourceDirectory, "Debuggee");
        string content = "#!/bin/sh\nprintf 'complete\\n'\nexit 0\n" + new string('#', 8 * 1024 * 1024);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            Directory.CreateDirectory(sourceDirectory);
            await File.WriteAllTextAsync(sourceExecutable, content, cancellationToken);
            File.SetUnixFileMode(sourceExecutable, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            using ManualResetEventSlim start = new();
            Task<string>[] preparations = Enumerable.Range(0, 16)
                .Select(_ => Task.Run(() =>
                {
                    start.Wait(cancellationToken);
                    return SnapshotStore.EnsureExecutable(sourceExecutable, overlayRoot, sourceRoot);
                }, cancellationToken))
                .ToArray();
            Task<string[]> allPreparations = Task.WhenAll(preparations);
            string expectedExecutable = Path.Combine(
                overlayRoot,
                Path.GetRelativePath(sourceRoot, sourceExecutable));

            start.Set();
            while (!File.Exists(expectedExecutable) && !allPreparations.IsCompleted)
            {
                await Task.Yield();
            }

            if (File.Exists(expectedExecutable))
            {
                Assert.Equal(content, await File.ReadAllTextAsync(expectedExecutable, cancellationToken));
                Assert.True((File.GetUnixFileMode(expectedExecutable) & UnixFileMode.UserExecute) != 0);
            }

            string[] executables = await allPreparations;
            string executable = Assert.Single(executables.Distinct());

            Assert.Equal(content, await File.ReadAllTextAsync(executable, cancellationToken));
            Assert.True((File.GetUnixFileMode(executable) & UnixFileMode.UserExecute) != 0);
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(executable)!,
                $".{Path.GetFileName(executable)}.*.tmp"));

            ProcessStartInfo startInfo = new(executable)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using Process process = Process.Start(startInfo)!;
            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            Assert.Equal(0, process.ExitCode);
            Assert.Equal("complete\n", output);

            await File.WriteAllTextAsync(
                sourceExecutable,
                "#!/bin/sh\nprintf 'replacement\\n'\n",
                cancellationToken);
            Assert.Equal(executable, SnapshotStore.EnsureExecutable(sourceExecutable, overlayRoot, sourceRoot));
            Assert.Equal(content, await File.ReadAllTextAsync(executable, cancellationToken));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}
