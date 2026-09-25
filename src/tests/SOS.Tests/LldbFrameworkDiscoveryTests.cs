// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class LldbFrameworkDiscoveryTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("sos-lldb-");

    [Theory]
    [InlineData("Xcode.app/Contents/Developer", "../SharedFrameworks")]
    [InlineData("CommandLineTools", "Library/PrivateFrameworks")]
    public void ResolvesSelectedDeveloperTools(string developerRelativePath, string frameworkRelativePath)
    {
        string developerDir = Directory.CreateDirectory(Path.Combine(_root.FullName, "Developer Tools", developerRelativePath)).FullName;
        Directory.CreateDirectory(Path.Combine(developerDir, "..", "SharedFrameworks"));
        string frameworkDirectory = CreateFramework(Path.Combine(developerDir, frameworkRelativePath));

        Assert.Equal(frameworkDirectory, ToolPaths.ResolveLldbFrameworkDirectory(developerDir));
    }

    [Fact]
    public void PrefersXcodeLayoutWhenBothFrameworksExist()
    {
        string developerDir = Directory.CreateDirectory(Path.Combine(_root.FullName, "Developer")).FullName;
        string xcodeFramework = CreateFramework(Path.Combine(developerDir, "..", "SharedFrameworks"));
        CreateFramework(Path.Combine(developerDir, "Library", "PrivateFrameworks"));

        Assert.Equal(xcodeFramework, ToolPaths.ResolveLldbFrameworkDirectory(developerDir));
    }

    [Fact]
    public void MissingFrameworkReportsSelectedDeveloperDirectory()
    {
        string developerDir = Directory.CreateDirectory(Path.Combine(_root.FullName, "Developer")).FullName;
        Directory.CreateDirectory(Path.Combine(developerDir, "..", "SharedFrameworks", "LLDB.framework"));
        Directory.CreateDirectory(Path.Combine(developerDir, "Library", "PrivateFrameworks", "LLDB.framework"));

        DirectoryNotFoundException error = Assert.Throws<DirectoryNotFoundException>(
            () => ToolPaths.ResolveLldbFrameworkDirectory(developerDir));

        Assert.Contains(developerDir, error.Message);
        Assert.Contains("DEVELOPER_DIR", error.Message);
    }

    private static string CreateFramework(string directory)
    {
        string frameworkDirectory = Directory.CreateDirectory(directory).FullName;
        string bundleDirectory = Directory.CreateDirectory(Path.Combine(frameworkDirectory, "LLDB.framework")).FullName;
        File.WriteAllText(Path.Combine(bundleDirectory, "LLDB"), string.Empty);
        return frameworkDirectory;
    }

    public void Dispose() => _root.Delete(recursive: true);
}
