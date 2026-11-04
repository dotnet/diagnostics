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
        string[] names =
        [
            @"C:\app\app.exe",
            @"C:\runtime\coreclr.dll",
            @"C:\runtime\clr.dll",
            "/runtime/libcoreclr.so",
            "/runtime/libcoreclr.dylib",
            @"C:\app\other.dll",
        ];
        for (int i = 0; i < names.Length; i++)
        {
            target.AddModule((ulong)(i + 1) * 0x1000, names[i]);
        }

        DotNetRuntimeDiscoveryProvider provider = new(target.Services);
        IModule[] modules = target.Services.GetService<IModuleService>().EnumerateModules().ToArray();
        Assert.Equal([0x2000UL, 0x3000UL, 0x4000UL, 0x5000UL],
            modules.Where(module => provider.TryDescribeRuntime(module, out _)).Select(module => module.ImageBase));
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
}
