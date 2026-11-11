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

    [Fact]
    public void LayoutInterfaceExposesOnlyState()
    {
        Assert.All(typeof(IToolLayout).GetMethods(), method => Assert.True(method.IsSpecialName));
    }

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
        Assert.Equal(typeof(SOS.Extensions.HostServices).Assembly.Location, layout.SosAssemblyPath);
    }

    [Theory]
    [InlineData("nuget")]
    [InlineData("NuGet")]
    [InlineData("SingleFile")]
    [InlineData("packaged")]
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

    [Fact]
    public void PackagedLayoutRequiresAnExplicitRoot()
    {
        Assert.Throws<ArgumentException>(() => ToolLayout.FromConfiguration("Packaged", null));
        Assert.Throws<ArgumentException>(() => ToolLayout.FromConfiguration("Packaged", " "));
    }

    [Fact]
    public void PackageInstallationRequiresMatchingToolVersions()
    {
        File.WriteAllText(Path.Combine(_root, "dotnet-sos.10.0.123.nupkg"), "");
        File.WriteAllText(Path.Combine(_root, "dotnet-dump.10.0.122.nupkg"), "");

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => PackagedToolSetup.GetPackageVersion(_root));

        Assert.Equal(Path.Combine(_root, "dotnet-dump.10.0.123.nupkg"), error.FileName);
        File.WriteAllText(error.FileName!, "");
        Assert.Equal("10.0.123", PackagedToolSetup.GetPackageVersion(_root));
    }

    [Fact]
    public void PackageInstallationRejectsAmbiguousVersions()
    {
        File.WriteAllText(Path.Combine(_root, "dotnet-sos.10.0.123.nupkg"), "");
        File.WriteAllText(Path.Combine(_root, "dotnet-sos.10.0.124.nupkg"), "");

        Assert.Throws<InvalidDataException>(() => PackagedToolSetup.GetPackageVersion(_root));
    }

    [Fact]
    public void PackageInstallationRejectsMissingPackages()
    {
        Assert.Throws<InvalidDataException>(() => PackagedToolSetup.GetPackageVersion(_root));
    }

    [Fact]
    public void PackagedLayoutUsesOnlyPreparedInputs()
    {
        CreatePackagedInputs();
        IToolLayout layout = ToolLayout.FromConfiguration("Packaged", _root);

        Assert.IsType<PackagedToolLayout>(layout);
        Assert.Equal(Path.Combine(_root, "sos"), layout.SosDirectory);
        Assert.Equal(Path.Combine(_root, "test-artifacts", "dumps"), layout.DumpCacheDirectory);
        Assert.Equal(Path.Combine(_root, "sos", "net8.0", "SOS.Extensions.dll"), layout.SosAssemblyPath);
        Assert.Equal(Path.Combine(_root, "tools", "dotnet-dump" + RepoLayout.ExeSuffix), layout.DotNetDump.FileName);
        Assert.Null(layout.DotNetDump.AssemblyPath);
        string dumpPath = Path.Combine(_root, "a dump.dmp");
        ProcessStartInfo process = DotNetDumpHost.CreateStartInfo(layout, dumpPath);
        Assert.Equal(Path.Combine(_root, "tools", "dotnet-dump" + RepoLayout.ExeSuffix), process.FileName);
        Assert.Equal(["analyze", dumpPath], process.ArgumentList);
        Assert.False(process.UseShellExecute);
        Assert.True(process.RedirectStandardInput);
        Assert.True(process.RedirectStandardOutput);
        Assert.True(process.RedirectStandardError);
        Assert.True(process.CreateNoWindow);
        Assert.Equal(RepoLayout.DotNetRoot, process.Environment["DOTNET_ROOT"]);
        Assert.Equal(RepoLayout.DotNetRoot, process.Environment[$"DOTNET_ROOT_{RepoLayout.TargetArch.ToUpperInvariant()}"]);
    }

    [Theory]
    [InlineData("native")]
    [InlineData("managed")]
    [InlineData("dump")]
    public void MissingPackagedInputsNeverFallBackToBin(string missing)
    {
        Dictionary<string, string> inputs = CreatePackagedInputs();
        File.Delete(inputs[missing]);
        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => ToolLayout.FromConfiguration("Packaged", _root));
        Assert.Equal(inputs[missing], error.FileName);
    }

    [Fact]
    public void ManagedToolArgumentsPreservePathsWithSpaces()
    {
        string dotnet = Path.Combine(_root, "dotnet host.exe");
        string dll = Path.Combine(_root, "tool directory", "dotnet-dump.dll");
        TestToolLayout layout = new(_root, Path.Combine(_root, "SOS.Extensions.dll"), new ToolCommand(dotnet, dll), _root, null);
        ProcessStartInfo process = DotNetDumpHost.CreateStartInfo(layout, Path.Combine(_root, "a dump.dmp"));

        Assert.False(process.UseShellExecute);
        Assert.Equal(dotnet, process.FileName);
        Assert.Equal([dll, "analyze", Path.Combine(_root, "a dump.dmp")], process.ArgumentList);
    }

    [Fact]
    public void CaptureEnvironmentPreservesDebuggeeRuntime()
    {
        CreatePackagedInputs();
        IToolLayout layout = ToolLayout.FromConfiguration("Packaged", _root);
        ProcessStartInfo debuggee = new("debuggee");
        debuggee.Environment["DOTNET_ROOT"] = "target runtime";
        debuggee.Environment[$"DOTNET_ROOT_{RepoLayout.TargetArch.ToUpperInvariant()}"] = "target architecture runtime";
        debuggee.Environment["SOSHARNESS_DOTNETDUMP_DLL"] = "inherited.dll";

        SnapshotStore.ConfigureCaptureEnvironment(layout, debuggee);

        Assert.Equal("target runtime", debuggee.Environment["DOTNET_ROOT"]);
        Assert.Equal("target architecture runtime", debuggee.Environment[$"DOTNET_ROOT_{RepoLayout.TargetArch.ToUpperInvariant()}"]);
        Assert.Equal(RepoLayout.DotNetRoot, debuggee.Environment["SOSHARNESS_DOTNET_ROOT"]);
        Assert.Equal(Path.Combine(_root, "tools", "dotnet-dump" + RepoLayout.ExeSuffix), debuggee.Environment["SOSHARNESS_DOTNETDUMP"]);
        Assert.Null(debuggee.Environment["SOSHARNESS_DOTNETDUMP_DLL"]);
    }

    [Fact]
    public void ProcessConfigurationPreservesInheritedBundleExtraction()
    {
        CreatePackagedInputs();
        IToolLayout layout = ToolLayout.FromConfiguration("Packaged", _root);
        string? inheritedExtraction = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
        ProcessStartInfo process = DotNetDumpHost.CreateStartInfo(layout, Path.Combine(_root, "a dump.dmp"));
        ProcessStartInfo debuggee = new("debuggee");

        SnapshotStore.ConfigureCaptureEnvironment(layout, debuggee);

        process.Environment.TryGetValue("DOTNET_BUNDLE_EXTRACT_BASE_DIR", out string? toolExtraction);
        debuggee.Environment.TryGetValue("DOTNET_BUNDLE_EXTRACT_BASE_DIR", out string? captureExtraction);
        Assert.Equal(inheritedExtraction, toolExtraction);
        Assert.Equal(inheritedExtraction, captureExtraction);
        Assert.Equal(inheritedExtraction, Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"));
    }

    [Fact]
    public void ManagedToolCaptureEnvironmentIncludesDllWithoutChangingTargetRuntime()
    {
        string dotnet = Path.Combine(_root, "dotnet.exe");
        string dll = Path.Combine(_root, "dotnet-dump.dll");
        ProcessStartInfo debuggee = new("debuggee");
        debuggee.Environment["DOTNET_ROOT"] = "target runtime";

        TestToolLayout layout = new(_root, Path.Combine(_root, "SOS.Extensions.dll"), new ToolCommand(dotnet, dll), _root, null);
        SnapshotStore.ConfigureCaptureEnvironment(layout, debuggee);

        Assert.Equal(dotnet, debuggee.Environment["SOSHARNESS_DOTNETDUMP"]);
        Assert.Equal(dll, debuggee.Environment["SOSHARNESS_DOTNETDUMP_DLL"]);
        Assert.Equal(RepoLayout.DotNetRoot, debuggee.Environment["SOSHARNESS_DOTNET_ROOT"]);
        Assert.Equal("target runtime", debuggee.Environment["DOTNET_ROOT"]);
    }

    [Fact]
    public void BinLoadsTheHarnessReferencedSosAssembly()
    {
        IToolLayout layout = ToolLayout.FromConfiguration("Bin", null);
        Assert.Same(typeof(SOS.Extensions.HostServices).Assembly, DbgEngSosHost.LoadSosAssembly(layout));
    }

    [Fact]
    public void PackagedAssemblyLoadingNeverFallsBackWhenItsAssemblyIsInvalid()
    {
        CreatePackagedInputs();
        IToolLayout layout = ToolLayout.FromConfiguration("Packaged", _root);
        Assert.Throws<BadImageFormatException>(() => DbgEngSosHost.LoadSosAssembly(layout));
    }

    [Fact]
    public void PackagedAssemblyLoadingDoesNotFallBackToHarnessAssemblies()
    {
        DbgEngSosHost.SosLoadContext context = new(_root);
        Assert.Throws<FileNotFoundException>(() => context.LoadFromAssemblyName(typeof(RepoLayout).Assembly.GetName()));
        Assert.Same(typeof(object).Assembly, context.LoadFromAssemblyName(typeof(object).Assembly.GetName()));
    }

    [Fact]
    public void PackagedAssemblyLoadingUsesItsOwnProductAssembly()
    {
        string assemblyPath = typeof(SOS.Extensions.HostServices).Assembly.Location;
        DbgEngSosHost.SosLoadContext context = new(Path.GetDirectoryName(assemblyPath)!);
        Assembly assembly = context.LoadFromAssemblyName(typeof(SOS.Extensions.HostServices).Assembly.GetName());
        Assert.Equal(assemblyPath, assembly.Location);
        Assert.NotSame(typeof(SOS.Extensions.HostServices).Assembly, assembly);
    }

    [Theory]
    [InlineData(Host.Cdb)]
    [InlineData(Host.Lldb)]
    [InlineData(Host.DotnetDump)]
    public void CDacPreparationCopiesOverridesToTheSelectedHost(Host host)
    {
        IToolLayout layout = CreateOverrideLayout();
        string destination = host == Host.DotnetDump
            ? Path.Combine(Path.GetDirectoryName(layout.DotNetDump.AssemblyPath)!, RepoLayout.Rid)
            : layout.SosDirectory;
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, CDacOverride.DacFileName), "old dac");
        File.WriteAllText(Path.Combine(destination, CDacOverride.DbiFileName), "old dbi");

        CDacOverride.Prepare(layout, host);

        Assert.Equal("new dac", File.ReadAllText(Path.Combine(destination, CDacOverride.DacFileName)));
        Assert.Equal("new dbi", File.ReadAllText(Path.Combine(destination, CDacOverride.DbiFileName)));
        Assert.Equal("new dac", File.ReadAllText(Path.Combine(layout.CDacOverrideDirectory!, CDacOverride.DacFileName)));
        Assert.Equal("new dbi", File.ReadAllText(Path.Combine(layout.CDacOverrideDirectory!, CDacOverride.DbiFileName)));
    }

    [Fact]
    public void CDacPreparationWithoutAnOverrideDoesNotModifyTheLayout()
    {
        CreatePackagedInputs();
        IToolLayout layout = ToolLayout.FromConfiguration("Packaged", _root);

        CDacOverride.Prepare(layout, Host.Cdb);

        Assert.False(File.Exists(Path.Combine(layout.SosDirectory, CDacOverride.DacFileName)));
        Assert.False(File.Exists(Path.Combine(layout.SosDirectory, CDacOverride.DbiFileName)));
    }

    [Fact]
    public void CDacPreparationCanRetryAfterAMissingOverrideFile()
    {
        IToolLayout layout = CreateOverrideLayout();
        Directory.CreateDirectory(layout.SosDirectory);
        string dbiSource = Path.Combine(layout.CDacOverrideDirectory!, CDacOverride.DbiFileName);
        File.Delete(dbiSource);

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => CDacOverride.Prepare(layout, Host.Cdb));
        Assert.Equal(dbiSource, error.FileName);

        File.WriteAllText(dbiSource, "restored dbi");
        CDacOverride.Prepare(layout, Host.Cdb);

        Assert.Equal("restored dbi", File.ReadAllText(Path.Combine(layout.SosDirectory, CDacOverride.DbiFileName)));
    }

    [Fact]
    public void CDacPreparationRejectsAnExecutableOnlyDotnetDump()
    {
        TestToolLayout layout = CreateOverrideLayout();
        layout = layout with { DotNetDump = new ToolCommand("dotnet-dump.exe") };

        Assert.Throws<InvalidOperationException>(() => CDacOverride.Prepare(layout, Host.DotnetDump));
    }

    private TestToolLayout CreateOverrideLayout()
    {
        string source = Directory.CreateDirectory(Path.Combine(_root, "cdac-source")).FullName;
        File.WriteAllText(Path.Combine(source, CDacOverride.DacFileName), "new dac");
        File.WriteAllText(Path.Combine(source, CDacOverride.DbiFileName), "new dbi");
        return new TestToolLayout(
            Path.Combine(_root, "sos"),
            Path.Combine(_root, "sos", "SOS.Extensions.dll"),
            new ToolCommand("dotnet", Path.Combine(_root, "dump-tool", "dotnet-dump.dll")),
            Path.Combine(_root, "dumps"),
            source);
    }

    private sealed record TestToolLayout(
        string SosDirectory,
        string SosAssemblyPath,
        ToolCommand DotNetDump,
        string DumpCacheDirectory,
        string? CDacOverrideDirectory) : IToolLayout;

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
