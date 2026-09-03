// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime;
using Xunit;

public class RuntimeProviderTests
{
    [Fact]
    public void NamedRuntimeModulesAreSelected()
    {
        TestModuleInfo[] modules =
        [
            new(0x1000, @"C:\app\app.exe"),
            new(0x2000, @"C:\runtime\coreclr.dll"),
            new(0x3000, @"C:\runtime\clr.dll"),
            new(0x4000, "/runtime/libcoreclr.so"),
            new(0x5000, "/runtime/libcoreclr.dylib"),
            new(0x6000, @"C:\app\other.dll"),
        ];

        IReadOnlyList<ModuleInfo> result = RuntimeProvider.EnumerateRuntimeModules(
            new TestDataReader(OSPlatform.Windows, modules),
            RuntimeEnumerationFlags.Default,
            entryPointBase: 0x1000,
            entryPointFileName: modules[0].FileName);

        Assert.Equal([0x2000UL, 0x3000UL, 0x4000UL, 0x5000UL], result.Select(module => module.ImageBase));
    }

    [Fact]
    public void EntrypointRuntimeExportsAreSelected()
    {
        TestModuleInfo[] modules =
        [
            new(0x1000, @"C:\app\sample.exe", "DotNetRuntimeInfo"),
            new(0x2000, @"C:\app\sample.dll", "DotNetRuntimeContractDescriptor"),
            new(0x3000, @"C:\other\other.dll", "DotNetRuntimeContractDescriptor"),
            new(0x4000, @"C:\runtime\coreclr.dll"),
        ];

        IReadOnlyList<ModuleInfo> result = RuntimeProvider.EnumerateRuntimeModules(
            new TestDataReader(OSPlatform.Windows, modules),
            RuntimeEnumerationFlags.Default,
            entryPointBase: 0x1000,
            entryPointFileName: modules[0].FileName);

        Assert.Equal([0x1000UL, 0x2000UL, 0x4000UL], result.Select(module => module.ImageBase));
    }

    [Fact]
    public void AllReturnsEveryModule()
    {
        TestModuleInfo[] modules =
        [
            new(0x1000, "app"),
            new(0x2000, "other"),
        ];

        IReadOnlyList<ModuleInfo> result = RuntimeProvider.EnumerateRuntimeModules(
            new TestDataReader(OSPlatform.Linux, modules),
            RuntimeEnumerationFlags.All,
            entryPointBase: null,
            entryPointFileName: null);

        Assert.Same(modules[0], result[0]);
        Assert.Same(modules[1], result[1]);
    }

    [Theory]
    [InlineData("DotNetRuntimeContractDescriptor")]
    [InlineData("DotNetRuntimeDebugHeader")]
    public void NativeAotEntrypointMarkersAreSelected(string marker)
    {
        TestModuleInfo entryPoint = new(0x1000, @"C:\app\native.exe", marker);

        IReadOnlyList<ModuleInfo> result = RuntimeProvider.EnumerateRuntimeModules(
            new TestDataReader(OSPlatform.Windows, [entryPoint]),
            RuntimeEnumerationFlags.Default,
            entryPoint.ImageBase,
            entryPoint.FileName);

        Assert.Same(entryPoint, Assert.Single(result));
    }

    [Fact]
    public void NativeAotOutsideEntrypointRequiresAll()
    {
        TestModuleInfo nativeAot = new(0x2000, @"C:\other\native.dll", "DotNetRuntimeContractDescriptor");
        TestDataReader reader = new(OSPlatform.Windows, [nativeAot]);

        Assert.Empty(RuntimeProvider.EnumerateRuntimeModules(
            reader,
            RuntimeEnumerationFlags.Default,
            entryPointBase: 0x1000,
            entryPointFileName: @"C:\app\app.exe"));

        Assert.Same(nativeAot, Assert.Single(RuntimeProvider.EnumerateRuntimeModules(
            reader,
            RuntimeEnumerationFlags.All,
            entryPointBase: 0x1000,
            entryPointFileName: @"C:\app\app.exe")));
    }

    [Fact]
    public void ExportProbeFailureDoesNotSelectEntrypoint()
    {
        TestModuleInfo entryPoint = new(0x1000, @"C:\app\sample.exe", throwOnExport: true);

        IReadOnlyList<ModuleInfo> result = RuntimeProvider.EnumerateRuntimeModules(
            new TestDataReader(OSPlatform.Windows, [entryPoint]),
            RuntimeEnumerationFlags.Default,
            entryPoint.ImageBase,
            entryPoint.FileName);

        Assert.Empty(result);
    }

    private sealed class TestModuleInfo : ModuleInfo
    {
        private readonly HashSet<string> _exports;
        private readonly bool _throwOnExport;

        public TestModuleInfo(
            ulong imageBase,
            string fileName,
            string export = null,
            bool throwOnExport = false)
            : base(imageBase, fileName)
        {
            _exports = export is null ? [] : [export];
            _throwOnExport = throwOnExport;
        }

        public override ModuleKind Kind => ModuleKind.Unknown;

        public override ulong GetExportSymbolAddress(string symbol)
        {
            if (_throwOnExport)
            {
                throw new InvalidDataException();
            }
            return _exports.Contains(symbol) ? ImageBase + 0x100 : 0;
        }
    }

    private sealed class TestDataReader : IDataReader
    {
        private readonly IReadOnlyList<ModuleInfo> _modules;

        public TestDataReader(OSPlatform targetPlatform, IReadOnlyList<ModuleInfo> modules)
        {
            TargetPlatform = targetPlatform;
            _modules = modules;
        }

        public string DisplayName => nameof(TestDataReader);
        public bool IsThreadSafe => true;
        public OSPlatform TargetPlatform { get; }
        public Architecture Architecture => Architecture.X64;
        public int ProcessId => 1;
        public int PointerSize => sizeof(long);
        public IEnumerable<ModuleInfo> EnumerateModules() => _modules;
        public bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context) => false;
        public void FlushCachedData() { }
        public int Read(ulong address, Span<byte> buffer) => 0;
        public bool Read<T>(ulong address, out T value) where T : unmanaged
        {
            value = default;
            return false;
        }
        public T Read<T>(ulong address) where T : unmanaged => default;
        public bool ReadPointer(ulong address, out ulong value)
        {
            value = 0;
            return false;
        }
        public ulong ReadPointer(ulong address) => 0;
    }
}
