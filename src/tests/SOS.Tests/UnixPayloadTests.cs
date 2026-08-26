// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
    public void NonExecutableTargetUsesWritableOverlay()
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
            File.SetUnixFileMode(sourceExecutable, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            string executable = SnapshotStore.EnsureExecutable(sourceExecutable, overlayRoot, sourceRoot);

            Assert.NotEqual(sourceExecutable, executable);
            Assert.Equal("executable", File.ReadAllText(executable));
            Assert.Equal("sidecar", File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "Debuggee.dll")));
            Assert.True((File.GetUnixFileMode(executable) & UnixFileMode.UserExecute) != 0);
            Assert.True((File.GetUnixFileMode(sourceExecutable) & UnixFileMode.UserExecute) == 0);

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
}
