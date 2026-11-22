// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.DebugServices.UnitTests;
using Xunit;

public class RuntimeProviderTests
{
    [Fact]
    public void NormalProviderRecognizesOnlyNamedRuntimeModules()
    {
        using RuntimeDiscoveryTestTarget target = new(OSPlatform.Windows, Architecture.X64);
        TestModuleInfo[] modules =
        [
            new(0x1000, @"C:\app\app.exe"),
            new(0x2000, @"C:\runtime\coreclr.dll"),
            new(0x3000, @"C:\runtime\clr.dll"),
            new(0x4000, "/runtime/libcoreclr.so"),
            new(0x5000, "/runtime/libcoreclr.dylib"),
            new(0x6000, @"C:\app\other.dll"),
        ];
        foreach (TestModuleInfo module in modules)
        {
            target.AddModule(module.ImageBase, module.FileName);
        }

        DotNetRuntimeDiscoveryProvider provider = new(target.Services);
        TestModuleInfo[] recognizedModules = target.Services.GetService<IModuleService>().EnumerateModules()
            .Where(module => provider.TryDescribeRuntime(module, out _))
            .Select(module => new TestModuleInfo(module.ImageBase, module.FileName))
            .ToArray();
        Assert.Equal([modules[1], modules[2], modules[3], modules[4]], recognizedModules);
        Assert.DoesNotContain(target.ExportProbes, probe => probe.ModuleBase is 0x1000 or 0x6000);
        target.Symbols.Verify();
    }

    [Fact]
    public void WindowsEntrypointRunsBeforeOtherModules()
    {
        using RuntimeDiscoveryTestTarget target = new(OSPlatform.Windows, Architecture.X64);
        target.AddModule(0x10000, @"C:\app\native.exe", needsSymbols: true);
        target.AddModule(0x20000, @"C:\app\sample.dll", needsSymbols: true);
        RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x30000, @"C:\app\sample.exe", entryPoint: true);
        target.AddSingleFileRuntime(app);

        Assert.Equal(app.ImageBase, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
        Assert.All(target.ExportProbes, probe => Assert.Equal(app.ImageBase, probe.ModuleBase));
        target.Symbols.Verify();
    }

    [Theory]
    [InlineData("WINDOWS", RuntimeEnumerationFlags.Default)]
    [InlineData("WINDOWS", RuntimeEnumerationFlags.All)]
    [InlineData("LINUX", RuntimeEnumerationFlags.Default)]
    [InlineData("LINUX", RuntimeEnumerationFlags.All)]
    [InlineData("OSX", RuntimeEnumerationFlags.Default)]
    [InlineData("OSX", RuntimeEnumerationFlags.All)]
    public void NormalMatchSkipsHintedModulesUnlessAllIsRequested(string platform, RuntimeEnumerationFlags flags)
    {
        OSPlatform os = OSPlatform.Create(platform);
        using RuntimeDiscoveryTestTarget target = new(os, Architecture.X64);
        target.AddModule(0x10000, os == OSPlatform.Windows ? @"C:\app\native.exe" : "/app/native", needsSymbols: true);
        RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000,
            os == OSPlatform.Windows ? @"C:\app\singlefile.exe" : "/app/singlefile", entryPoint: true);
        target.AddSingleFileRuntime(app);
        target.AddDiagnosticHeader(app.ImageBase);
        target.AddCoreRuntime(0x30000, diagnosticHeader: false);
        if (flags == RuntimeEnumerationFlags.All)
        {
            target.Symbols.ExpectSymbolFile(0x10000, result: null);
        }

        DescriptionBasedRuntimeProvider provider = new(target.Services);
        RuntimeDescription[] descriptions = provider.EnumerateRuntimeDescriptions(
            [new DotNetRuntimeDiscoveryProvider(target.Services), new SingleFileRuntimeDiscoveryProvider(target.Services)],
            flags).ToArray();

        Assert.Equal(flags == RuntimeEnumerationFlags.All ? [0x30000UL, 0x20000UL] : new[] { 0x30000UL },
            descriptions.Select(description => description.Module.ImageBase));
        if (flags == RuntimeEnumerationFlags.Default)
        {
            Assert.DoesNotContain(target.ExportProbes, probe => probe.ModuleBase is 0x10000 or 0x20000);
        }
        target.Symbols.Verify();
    }

    [Fact]
    public void SingleFileDiscoveryOnlyRunsWhenRegistered()
    {
        using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64);
        RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x10000, "/app/singlefile", needsSymbols: true);
        target.AddSingleFileRuntime(app, symbolOnly: true);

        DescriptionBasedRuntimeProvider provider = new(target.Services);
        Assert.Empty(provider.EnumerateRuntimeDescriptions([new DotNetRuntimeDiscoveryProvider(target.Services)], RuntimeEnumerationFlags.All));
        Assert.Empty(target.ExportProbes);
        Assert.Empty(target.Symbols.Requests);

        target.Symbols.ExpectSymbolFile(app.ImageBase, result: "mock-symbols");
        RuntimeDescription description = Assert.Single(provider.EnumerateRuntimeDescriptions(
            [new SingleFileRuntimeDiscoveryProvider(target.Services)], RuntimeEnumerationFlags.All));
        Assert.Equal(app.ImageBase, description.Module.ImageBase);
        Assert.True(description.IsSingleFile);
        target.Symbols.Verify();
    }

    [Fact]
    public void NoRegisteredProvidersProbeNoModules()
    {
        using RuntimeDiscoveryTestTarget target = new(OSPlatform.Windows, Architecture.X64);
        target.AddModule(0x10000, @"C:\app\sample.exe", entryPoint: true, needsSymbols: true);
        DescriptionBasedRuntimeProvider provider = new(target.Services);
        Assert.Empty(provider.EnumerateRuntimeDescriptions([], RuntimeEnumerationFlags.All));
        Assert.Empty(target.ExportProbes);
        target.Symbols.Verify();
    }

    private readonly record struct TestModuleInfo(ulong ImageBase, string FileName);
}
