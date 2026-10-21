// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class ToolLayoutTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("sos tool layout ").FullName;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bin")]
    public void BinIsTheDefault(string? value)
    {
        IToolLayout layout = ToolLayout.FromConfiguration(value, null);
        Assert.IsType<BinToolLayout>(layout);
        Assert.Equal(RepoLayout.ArtifactsBinNative, layout.SosDirectory);
        Assert.Equal(Path.Combine(RepoLayout.Scratch, "dumps"), layout.DumpCacheDirectory);
    }

    [Theory]
    [InlineData("nuget")]
    [InlineData("1")]
    [InlineData("Unknown")]
    [InlineData(" Bin")]
    public void UnknownLayoutsAreRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => ToolLayout.FromConfiguration(value, _root));
    }

    [Fact]
    public void BinRejectsPackagedRoot()
    {
        Assert.Throws<ArgumentException>(() => ToolLayout.FromConfiguration("Bin", _root));
    }

    [Theory]
    [InlineData("NuGet")]
    [InlineData("SingleFile")]
    public void PackagedLayoutsRequireAnExplicitRoot(string value)
    {
        Assert.Throws<ArgumentException>(() => ToolLayout.FromConfiguration(value, null));
        Assert.Throws<ArgumentException>(() => ToolLayout.FromConfiguration(value, " "));
    }

    [Theory]
    [InlineData("NuGet", typeof(NuGetToolLayout))]
    [InlineData("SingleFile", typeof(SingleFileToolLayout))]
    public void PackagedLayoutsUseOnlyPreparedInputs(string value, Type expected)
    {
        CreatePackagedInputs();
        IToolLayout layout = ToolLayout.FromConfiguration(value, _root);

        Assert.Equal(expected, layout.GetType());
        Assert.Equal(Path.Combine(_root, "sos"), layout.SosDirectory);
        Assert.Equal(Path.Combine(_root, "sos", "net8.0"), layout.ManagedSosDirectory);
        Assert.Equal(Path.Combine(_root, "test-artifacts", "dumps"), layout.DumpCacheDirectory);
        ProcessStartInfo process = layout.CreateDotNetDumpStartInfo();
        Assert.Equal(Path.Combine(_root, "tools", "dotnet-dump" + RepoLayout.ExeSuffix), process.FileName);
        Assert.Empty(process.ArgumentList);
        Assert.False(process.UseShellExecute);
        Assert.Equal(RepoLayout.DotNetRoot, process.Environment["DOTNET_ROOT"]);
        Assert.Equal(RepoLayout.DotNetRoot, process.Environment[$"DOTNET_ROOT_{RepoLayout.TargetArch.ToUpperInvariant()}"]);
    }

    [Theory]
    [InlineData("NuGet", "native")]
    [InlineData("NuGet", "managed")]
    [InlineData("NuGet", "dump")]
    [InlineData("SingleFile", "native")]
    [InlineData("SingleFile", "managed")]
    [InlineData("SingleFile", "dump")]
    public void MissingPackagedInputsNeverFallBackToBin(string value, string missing)
    {
        Dictionary<string, string> inputs = CreatePackagedInputs();
        File.Delete(inputs[missing]);
        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => ToolLayout.FromConfiguration(value, _root));
        Assert.Equal(inputs[missing], error.FileName);
    }

    [Fact]
    public void ManagedToolArgumentsPreservePathsWithSpaces()
    {
        string dotnet = Path.Combine(_root, "dotnet host.exe");
        string dll = Path.Combine(_root, "tool directory", "dotnet-dump.dll");
        ProcessStartInfo process = new ToolCommand(dotnet, dll).CreateStartInfo();
        process.ArgumentList.Add("analyze");
        process.ArgumentList.Add(Path.Combine(_root, "a dump.dmp"));

        Assert.False(process.UseShellExecute);
        Assert.Equal(dotnet, process.FileName);
        Assert.Equal([dll, "analyze", Path.Combine(_root, "a dump.dmp")], process.ArgumentList);
    }

    [Theory]
    [InlineData("NuGet")]
    [InlineData("SingleFile")]
    public void CaptureEnvironmentPreservesDebuggeeRuntime(string value)
    {
        CreatePackagedInputs();
        IToolLayout layout = ToolLayout.FromConfiguration(value, _root);
        ProcessStartInfo debuggee = new("debuggee");
        debuggee.Environment["DOTNET_ROOT"] = "target runtime";
        debuggee.Environment[$"DOTNET_ROOT_{RepoLayout.TargetArch.ToUpperInvariant()}"] = "target architecture runtime";
        debuggee.Environment["SOSHARNESS_DOTNETDUMP_DLL"] = "inherited.dll";

        layout.ConfigureCaptureEnvironment(debuggee);

        Assert.Equal("target runtime", debuggee.Environment["DOTNET_ROOT"]);
        Assert.Equal("target architecture runtime", debuggee.Environment[$"DOTNET_ROOT_{RepoLayout.TargetArch.ToUpperInvariant()}"]);
        Assert.Equal(RepoLayout.DotNetRoot, debuggee.Environment["SOSHARNESS_DOTNET_ROOT"]);
        Assert.Equal(Path.Combine(_root, "tools", "dotnet-dump" + RepoLayout.ExeSuffix), debuggee.Environment["SOSHARNESS_DOTNETDUMP"]);
        Assert.Null(debuggee.Environment["SOSHARNESS_DOTNETDUMP_DLL"]);
    }

    [Theory]
    [InlineData("NuGet")]
    [InlineData("SingleFile")]
    public void LayoutOwnsBundleExtractionForToolsAndCapture(string value)
    {
        CreatePackagedInputs();
        IToolLayout layout = ToolLayout.FromConfiguration(value, _root);
        string? inheritedExtraction = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        ProcessStartInfo process = layout.CreateDotNetDumpStartInfo();
        ProcessStartInfo debuggee = new("debuggee");

        layout.ConfigureCaptureEnvironment(debuggee);

        string? expected = value == "SingleFile" ? Path.Combine(_root, "bundle-extract") : inheritedExtraction;
        process.Environment.TryGetValue("DOTNET_BUNDLE_EXTRACT_BASE_DIR", out string? toolExtraction);
        debuggee.Environment.TryGetValue("DOTNET_BUNDLE_EXTRACT_BASE_DIR", out string? captureExtraction);
        Assert.Equal(expected, toolExtraction);
        Assert.Equal(expected, captureExtraction);
        Assert.Equal(inheritedExtraction, Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"));
    }

    [Fact]
    public void ManagedToolCaptureEnvironmentIncludesDllWithoutChangingTargetRuntime()
    {
        string dotnet = Path.Combine(_root, "dotnet.exe");
        string dll = Path.Combine(_root, "dotnet-dump.dll");
        ProcessStartInfo debuggee = new("debuggee");
        debuggee.Environment["DOTNET_ROOT"] = "target runtime";

        new ToolCommand(dotnet, dll).ConfigureCaptureEnvironment(debuggee);

        Assert.Equal(dotnet, debuggee.Environment["SOSHARNESS_DOTNETDUMP"]);
        Assert.Equal(dll, debuggee.Environment["SOSHARNESS_DOTNETDUMP_DLL"]);
        Assert.Equal(RepoLayout.DotNetRoot, debuggee.Environment["SOSHARNESS_DOTNET_ROOT"]);
        Assert.Equal("target runtime", debuggee.Environment["DOTNET_ROOT"]);
    }

    [Fact]
    public void BinLoadsTheHarnessReferencedSosAssembly()
    {
        IToolLayout layout = ToolLayout.FromConfiguration("Bin", null);
        Assert.Same(typeof(SOS.Extensions.HostServices).Assembly, layout.LoadSosAssembly());
    }

    [Fact]
    public void PackagedAssemblyLoadingDoesNotFallBackToHarnessAssemblies()
    {
        PackagedToolLayout.SosLoadContext context = new(_root);
        Assert.Throws<FileNotFoundException>(() => context.LoadFromAssemblyName(typeof(RepoLayout).Assembly.GetName()));
        Assert.Same(typeof(object).Assembly, context.LoadFromAssemblyName(typeof(object).Assembly.GetName()));
    }

    [Fact]
    public void PackagedAssemblyLoadingUsesItsOwnProductAssembly()
    {
        string assemblyPath = typeof(SOS.Extensions.HostServices).Assembly.Location;
        PackagedToolLayout.SosLoadContext context = new(Path.GetDirectoryName(assemblyPath)!);
        Assembly assembly = context.LoadFromAssemblyName(typeof(SOS.Extensions.HostServices).Assembly.GetName());
        Assert.Equal(assemblyPath, assembly.Location);
        Assert.NotSame(typeof(SOS.Extensions.HostServices).Assembly, assembly);
    }

    private Dictionary<string, string> CreatePackagedInputs()
    {
        string nativeName = OperatingSystem.IsWindows() ? "sos.dll" :
            OperatingSystem.IsMacOS() ? "libsosplugin.dylib" : "libsosplugin.so";
        Dictionary<string, string> inputs = new()
        {
            ["native"] = Path.Combine(_root, "sos", nativeName),
            ["managed"] = Path.Combine(_root, "sos", "net8.0", "SOS.Extensions.dll"),
            ["dump"] = Path.Combine(_root, "tools", "dotnet-dump" + RepoLayout.ExeSuffix),
        };
        foreach (string path in inputs.Values)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
        }
        return inputs;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
