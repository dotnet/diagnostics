// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Text.Json;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class DumpBundleTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("sos dump bundle ").FullName;

    private static DumpKey Key => new(Flavor.Core, TargetCatalog.DivZero, GcType.Workstation, DumpKind.Heap, CoreVersion.Net11);
    private static DumpBundleEntry Entry => new(Key, "dumps", "debuggee/DivZero", "runtime", "11.0.0-source-patch");
    private static string[] Files => ["dumps/crash.dmp", "debuggee/DivZero", "debuggee/DivZero.pdb", "runtime/libcoreclr.so"];
    private static DumpBundleManifest Manifest => new(1, "linux-arm64", [Entry], Files);

    [Fact]
    public void BundleSurvivesRelocationWithoutTheProducerDirectory()
    {
        string source = WriteBundle(Manifest);
        string moved = Path.Combine(_root, "downloaded on another machine");
        Directory.Move(source, moved);

        DumpBundle bundle = DumpBundle.Load(moved);
        DumpArtifact dump = bundle.Resolve(Key, "crash");

        Assert.False(Directory.Exists(source));
        Assert.Equal("linux-arm64", bundle.Platform.Rid);
        Assert.Equal(Architecture.Arm64, bundle.Platform.Architecture);
        Assert.Equal(CoreVersion.Net11, bundle.AvailableCoreVersions);
        Assert.Equal(Path.Combine(moved, "dumps", "crash.dmp"), dump.DumpPath);
        Assert.Equal(Path.Combine(moved, "debuggee", "DivZero"), dump.ExecutablePath);
        Assert.Equal(Path.Combine(moved, "runtime"), dump.RuntimeDirectory);
        Assert.Equal([Path.Combine(moved, "debuggee"), Path.Combine(moved, "runtime")], dump.SymbolDirectories);
    }

    [Theory]
    [InlineData("dumps/crash.dmp")]
    [InlineData("debuggee/DivZero")]
    [InlineData("debuggee/DivZero.pdb")]
    [InlineData("runtime/libcoreclr.so")]
    public void MissingInputsFailBeforeAnyDebuggerIsOpened(string relative)
    {
        string root = WriteBundle(Manifest);
        string missing = Path.GetFullPath(Path.Combine(root, relative));
        File.Delete(missing);

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => DumpBundle.Load(root));
        Assert.Equal(missing, error.FileName);
    }

    [Fact]
    public void MissingConfigurationDoesNotCaptureOrUseLocalArtifacts()
    {
        DumpBundle bundle = DumpBundle.Load(WriteBundle(Manifest));

        Assert.Throws<FileNotFoundException>(() => bundle.Resolve(Key with { DumpKind = DumpKind.Full }, "crash"));
        Assert.Throws<ArgumentException>(() => bundle.Resolve(Key, "unknown-stop"));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("directory\\file")]
    [InlineData("directory/../outside")]
    [InlineData("directory//file")]
    [InlineData("./file")]
    public void ManifestPathsMustBePortableAndStayInsideTheBundle(string path)
    {
        string root = WriteBundle(Manifest with { Files = [path] });
        Assert.Throws<InvalidDataException>(() => DumpBundle.Load(root));
    }

    [Fact]
    public void ManifestRequiresSupportedVersionUniqueKeysAndMatchingRuntimeMajor()
    {
        Assert.Throws<InvalidDataException>(() => DumpBundle.Load(WriteBundle(Manifest with { FormatVersion = 2 })));
        Assert.Throws<InvalidDataException>(() => DumpBundle.Load(WriteBundle(Manifest with { Entries = [Entry, Entry] })));
        Assert.Throws<InvalidDataException>(() => DumpBundle.Load(WriteBundle(Manifest with { Entries = [Entry with { RuntimeVersion = "10.0.1" }] })));
        Assert.Throws<InvalidDataException>(() => DumpBundle.Load(WriteBundle(Manifest with { Entries = [Entry with { Key = Key with { GcType = GcType.AllValid } }] })));
        Assert.Throws<InvalidDataException>(() => DumpBundle.Load(WriteBundle(Manifest with { Files = Files.Skip(1).ToArray() })));
    }

    [Fact]
    public void ManifestRequiresAllFields()
    {
        string root = WriteBundle(Manifest);
        File.WriteAllText(Path.Combine(root, DumpBundle.ManifestFileName), """{"formatVersion":1,"sourceRid":"linux-arm64"}""");
        Assert.Throws<JsonException>(() => DumpBundle.Load(root));
    }

    [Fact]
    public void CaptureIdentityDoesNotIncludeAnalysisAxes()
    {
        TestConfig config = new(TargetCatalog.DivZero, Host.DotnetDump, Flavor.Core, Liveness.Dump, coreVersion: CoreVersion.Net11);
        Assert.Equal(DumpKey.From(config), DumpKey.From(config with { Host = Host.Lldb, Dac = Dac.CDac, HostRuntime = HostRuntime.None }));
        Assert.NotEqual(DumpKey.From(config), DumpKey.From(config with { GcType = GcType.Server }));
        Assert.NotEqual(DumpKey.From(config), DumpKey.From(config with { DumpKind = DumpKind.Full }));
    }

    [Theory]
    [InlineData("linux-arm64", "win-x64", Host.DotnetDump, Dac.CDac, CoreVersion.Net11, true)]
    [InlineData("win-x64", "linux-x64", Host.DotnetDump, Dac.CDac, CoreVersion.Net11, true)]
    [InlineData("linux-x64", "win-x64", Host.DotnetDump, Dac.Legacy, CoreVersion.Net11, false)]
    [InlineData("linux-x64", "win-x64", Host.DotnetDump, Dac.CDac, CoreVersion.Net10, false)]
    [InlineData("linux-x64", "win-x64", Host.Cdb, Dac.CDac, CoreVersion.Net11, false)]
    [InlineData("win-x64", "win-x64", Host.Cdb, Dac.Legacy, CoreVersion.Net10, true)]
    [InlineData("linux-x64", "linux-x64", Host.Lldb, Dac.Legacy, CoreVersion.Net10, true)]
    public void MatrixSeparatesSourceAndAnalysisPlatforms(string source, string analyzer, Host host, Dac dac, CoreVersion version, bool expected)
    {
        TestConfig config = new(TargetCatalog.DivZero, host, Flavor.Core, Liveness.Dump, coreVersion: version, dac: dac);
        Assert.Equal(expected, TestConfig.IsValid(config, new(source), new(analyzer), readOnly: true));
        Assert.False(TestConfig.IsValid(config with { Liveness = Liveness.Live }, new(source), new(analyzer), readOnly: true));
    }

    [Fact]
    public void SingleFileSupportDependsOnTheSourceRid()
    {
        TestConfig config = new(TargetCatalog.DivZero, Host.DotnetDump, Flavor.SingleFile, Liveness.Dump, coreVersion: CoreVersion.Net11, dac: Dac.CDac);
        Assert.False(TestConfig.IsValid(config, new("linux-musl-x64"), new("win-x64"), readOnly: true));
        Assert.True(TestConfig.IsValid(config, new("linux-x64"), new("win-x64"), readOnly: true));
    }

    [Fact]
    public void WindowsOnlyDebuggeesFollowTheSourceOS()
    {
        TestConfig config = new(TargetCatalog.ThreadApartment, Host.DotnetDump, Flavor.Core, Liveness.Dump, coreVersion: CoreVersion.Net11, dac: Dac.CDac);
        Assert.False(TestConfig.IsValid(config, new("linux-x64"), new("win-x64"), readOnly: true));
        Assert.True(TestConfig.IsValid(config, new("win-x64"), new("linux-x64"), readOnly: true));
    }

    [Fact]
    public void ManifestIdentityContainsOnlyCaptureDimensions()
    {
        string json = JsonSerializer.Serialize(Key, DumpBundle.JsonOptions);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(5, document.RootElement.EnumerateObject().Count());
        Assert.False(document.RootElement.TryGetProperty("relativeDirectory", out _));
    }

    [Fact]
    public void CaptureRejectsEmptySelectionsAndExistingOutputDirectories()
    {
        Assert.Throws<ArgumentException>(() => DumpBundle.Capture(Path.Combine(_root, "empty"), []));
        Assert.False(Directory.Exists(Path.Combine(_root, "empty")));
        TestConfig config = new(TargetCatalog.DivZero, Host.DotnetDump, Flavor.Core, Liveness.Dump);
        Assert.Throws<IOException>(() => DumpBundle.Capture(_root, [config]));
    }

    private string WriteBundle(DumpBundleManifest manifest)
    {
        string root = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (string relative in Files)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
        }

        File.WriteAllText(Path.Combine(root, DumpBundle.ManifestFileName), JsonSerializer.Serialize(manifest, DumpBundle.JsonOptions));
        return root;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
