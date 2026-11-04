// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Xunit;
using Xunit.Sdk;
using RuntimeInfo = Microsoft.Diagnostics.DebugServices.Implementation.RuntimeInfo;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class RuntimeDiscoveryTests
    {
        public static TheoryData<string, Architecture> Targets => new()
        {
            { "WINDOWS", Architecture.X86 },
            { "WINDOWS", Architecture.X64 },
            { "WINDOWS", Architecture.Arm64 },
            { "LINUX", Architecture.Arm },
            { "LINUX", Architecture.X64 },
            { "LINUX", Architecture.Arm64 },
            { "OSX", Architecture.X64 },
            { "OSX", Architecture.Arm64 },
        };

        public static IEnumerable<object[]> UnixHeaders =>
            Targets.Where(target => target.Data.Item1 != "WINDOWS").SelectMany(target =>
                (target.Data.Item1 == "OSX" ? new[] { false, true } : new[] { false })
                    .Select(legacy => new object[] { target.Data.Item1, target.Data.Item2, legacy }));

        public static IEnumerable<object[]> MetadataTargets =>
            Targets.SelectMany(target => new[] { 1, 2 }
                .Select(version => new object[] { target.Data.Item1, target.Data.Item2, version }));

        [Theory]
        [MemberData(nameof(Targets))]
        public void DefaultDiscoveryDoesNotRequestUnrelatedSymbols(string platform, Architecture architecture)
        {
            OSPlatform os = OSPlatform.Create(platform);
            using RuntimeDiscoveryTestTarget target = new(os, architecture);
            target.AddModule(0x10000, ApplicationName(os, "native"), needsSymbols: true);
            target.AddCoreRuntime(0x20000);
            target.AddCrashInfo(0x20000);
            target.AddModule(0x30000, ApplicationName(os, "plugin"), needsSymbols: true);
            target.AddModule(0x40000, ApplicationName(os, "app"), entryPoint: true);

            IRuntime runtime = Assert.Single(target.Enumerate());
            Assert.Equal(0x20000UL, runtime.RuntimeModule.ImageBase);
            Assert.Equal(RuntimeType.NetCore, runtime.RuntimeType);
            Assert.Equal(architecture, runtime.Target.Architecture);
            ClrInfo info = runtime.Services.GetService<ClrInfo>();
            Assert.Equal(new Version(11, 0, 1, 2), info.Version);
            Assert.Equal(4, info.DataTarget.EnumerateModules().Count());
            Assert.Same(runtime, Assert.Single(target.Enumerate()));
            Assert.Empty(target.Symbols.Requests);
            target.Symbols.Verify();
        }

        [Theory]
        [MemberData(nameof(Targets))]
        public void HigherPriorityProviderFindsRuntimeWithoutProbingSymbols(string platform, Architecture architecture)
        {
            OSPlatform os = OSPlatform.Create(platform);
            using RuntimeDiscoveryTestTarget target = new(os, architecture);
            target.AddModule(0x10000, ApplicationName(os, "native"), needsSymbols: true);
            target.AddCoreRuntime(0x20000, diagnosticHeader: false);
            target.AddModule(0x30000, ApplicationName(os, "plugin"), needsSymbols: true);

            Assert.Equal(0x20000UL, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Empty(target.Symbols.Requests);
            target.Symbols.Verify();
        }

        [Theory]
        [MemberData(nameof(Targets))]
        public void AllAllowsOnlyExpectedSymbolRequestsAndConstructsSingleFileMetadata(string platform, Architecture architecture)
        {
            OSPlatform os = OSPlatform.Create(platform);
            using RuntimeDiscoveryTestTarget target = new(os, architecture);
            target.AddModule(0x10000, ApplicationName(os, "native"), needsSymbols: true);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, ApplicationName(os, "app"), needsSymbols: true);
            target.AddSingleFileRuntime(app, symbolOnly: true);
            target.Symbols.ExpectSymbolFile(0x10000, result: null);
            target.Symbols.ExpectSymbolFile(0x20000, result: "mock-symbols");

            IRuntime runtime = Assert.Single(target.Enumerate(RuntimeEnumerationFlags.All));
            ClrInfo info = runtime.Services.GetService<ClrInfo>();
            Assert.True(info.IsSingleFile);
            Assert.Equal(new Version(11, 2, 3, 4), info.Version);
            Assert.Contains(info.DebuggingLibraries, library =>
                library.Kind == DebugLibraryKind.Dac &&
                library.ArchivedUnder == SymbolProperties.Self &&
                library.TargetArchitecture == architecture);
            if (os == OSPlatform.Windows)
            {
                Assert.Equal(0x23456789, info.IndexTimeStamp);
                Assert.Equal(0x2000, info.IndexFileSize);
            }
            else
            {
                Assert.Equal(new byte[] { 0x89, 0x67, 0x45, 0x23, 0, 0x20, 0, 0 }, info.BuildId);
            }
            Assert.Same(runtime, Assert.Single(target.Enumerate(RuntimeEnumerationFlags.All)));
            Assert.Equal([0x10000UL, 0x20000UL], target.Symbols.Requests);
            target.Symbols.Verify();
        }

        [Theory]
        [MemberData(nameof(UnixHeaders))]
        public void DiagnosticHintFindsSingleFileWithoutProbingLowerModules(string platform, Architecture architecture, bool legacyMacAddress)
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Create(platform), architecture);
            target.AddModule(0x10000, "/lib/native", needsSymbols: true);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, "/app/singlefile");
            target.AddSingleFileRuntime(app);
            target.AddDiagnosticHeader(app.ImageBase, legacyMacAddress: legacyMacAddress);

            IRuntime runtime = Assert.Single(target.Enumerate());
            Assert.Equal(app.ImageBase, runtime.RuntimeModule.ImageBase);
            Assert.True(runtime.Services.GetService<ClrInfo>().IsSingleFile);
            Assert.DoesNotContain(target.ExportProbes, probe => probe.ModuleBase == 0x10000);
            Assert.Empty(target.Symbols.Requests);
            target.Symbols.Verify();
        }

        [Theory]
        [MemberData(nameof(UnixHeaders))]
        public void UnusableDiagnosticHintFallsBackWithinSymbolBudget(string platform, Architecture architecture, bool legacyMacAddress)
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Create(platform), architecture);
            target.AddModule(0x10000, "/lib/native", needsSymbols: true);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, "/app/singlefile", needsSymbols: true);
            target.AddSingleFileRuntime(app, symbolOnly: true);
            target.AddDiagnosticHeader(0x90000, legacyMacAddress: legacyMacAddress);
            target.Symbols.ExpectSymbolFile(0x10000, result: null);
            target.Symbols.ExpectSymbolFile(app.ImageBase, result: "mock-symbols");

            Assert.Equal(app.ImageBase, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal([0x10000UL, 0x20000UL], target.Symbols.Requests);
            target.Symbols.Verify();

            target.Flush();
            target.Symbols.ExpectSymbolFile(0x10000, result: null);
            target.Symbols.ExpectSymbolFile(app.ImageBase, result: "mock-symbols");
            Assert.Equal(app.ImageBase, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal([0x10000UL, 0x20000UL, 0x10000UL, 0x20000UL], target.Symbols.Requests);
            target.Symbols.Verify();
        }

        [Theory]
        [MemberData(nameof(UnixHeaders))]
        public void UnrecognizedHintFallsBackWithoutProbingModulesTwice(string platform, Architecture architecture, bool legacyMacAddress)
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Create(platform), architecture);
            target.AddModule(0x10000, "/lib/native", needsSymbols: true);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, "/app/singlefile");
            target.AddSingleFileRuntime(app);
            target.AddDiagnosticHeader(0x10000, legacyMacAddress: legacyMacAddress);
            target.Symbols.ExpectSymbolFile(0x10000, result: null);

            Assert.Equal(app.ImageBase, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal([0x10000UL, 0x20000UL], target.ExportProbes
                .Where(probe => probe.Symbol == "DotNetRuntimeInfo").Select(probe => probe.ModuleBase));
            target.Symbols.Verify();
        }

        [Fact]
        public void UnexpectedSymbolRequestFailsInsteadOfBeingSilentlyIgnored()
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.Arm64);
            target.AddModule(0x10000, "/lib/native", needsSymbols: true);

            XunitException exception = Assert.ThrowsAny<XunitException>(() => target.Enumerate());
            Assert.Contains("Unexpected symbol-file request", exception.Message);
        }

        [Theory]
        [MemberData(nameof(MetadataTargets))]
        public void HostMetadataMatchesClrMdWithoutImplicitEnumeration(string platform, Architecture architecture, int headerVersion)
        {
            OSPlatform os = OSPlatform.Create(platform);
            using RuntimeDiscoveryTestTarget target = new(os, architecture);
            target.AddCoreRuntime(0x20000);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x30000, ApplicationName(os, "singlefile"));
            byte[] header = target.AddSingleFileRuntime(app);
            header[20] = (byte)headerVersion;
            if (os == OSPlatform.Windows)
            {
                target.AddModule(0x40000, @"C:\runtime\clr.dll");
            }

            ClrInfo[] actual = target.Enumerate(RuntimeEnumerationFlags.All)
                .Select(runtime => runtime.Services.GetService<ClrInfo>()).ToArray();
            int probes = target.ExportProbes.Count;
            Assert.Equal(actual.Length, actual[0].DataTarget.ClrVersions.Length);
            foreach (ClrInfo info in actual)
            {
                Assert.Same(actual[0].DataTarget, info.DataTarget);
                Assert.Contains(info, info.DataTarget.ClrVersions);
            }
            Assert.Equal(probes, target.ExportProbes.Count);

            using DataTarget reference = new(actual[0].DataTarget.DataReader, new DataTargetOptions
            {
                ForceCompleteRuntimeEnumeration = true,
            });
            ClrInfo[] expected = reference.ClrVersions.ToArray();
            Assert.Equal(expected.Select(info => info.ModuleInfo.ImageBase), actual.Select(info => info.ModuleInfo.ImageBase));
            foreach ((ClrInfo expectedInfo, ClrInfo actualInfo) in expected.Zip(actual))
            {
                Assert.Equal(expectedInfo.Flavor, actualInfo.Flavor);
                Assert.Equal(expectedInfo.IsSingleFile, actualInfo.IsSingleFile);
                Assert.Equal(expectedInfo.Version, actualInfo.Version);
                Assert.Equal(expectedInfo.IndexTimeStamp, actualInfo.IndexTimeStamp);
                Assert.Equal(expectedInfo.IndexFileSize, actualInfo.IndexFileSize);
                Assert.Equal(expectedInfo.BuildId, actualInfo.BuildId);
                Assert.Equal(expectedInfo.ContractDescriptorAddress, actualInfo.ContractDescriptorAddress);
                Assert.Equal(expectedInfo.DebuggingLibraries, actualInfo.DebuggingLibraries);
            }
            target.Symbols.Verify();
        }

        [Theory]
        [InlineData(1, 96)]
        [InlineData(2, 96)]
        [InlineData(2, 108)]
        [InlineData(2, 112)]
        public void SharedRuntimeInfoParserPreservesDiagnosticMetadata(int headerVersion, int headerSize)
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Windows, Architecture.X64);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, @"C:\app\singlefile.exe");
            byte[] header = target.AddSingleFileRuntime(app, headerSize: headerSize);
            header[20] = (byte)headerVersion;

            Assert.True(RuntimeInfo.TryRead(target.Services, app.Exports["DotNetRuntimeInfo"], out RuntimeInfo info));
            Assert.True(info.IsValid);
            Assert.Equal("DotNetRuntimeInfo", info.Signature);
            Assert.Equal(headerVersion, info.Version);
            Assert.Equal(header.AsSpan(0, 18).ToArray(), info.RawSignature);
            Assert.Equal(header.AsSpan(24, 24).ToArray(), info.RawRuntimeModuleIndex);
            Assert.Equal(header.AsSpan(48, 24).ToArray(), info.RawDacModuleIndex);
            Assert.Equal(header.AsSpan(72, 24).ToArray(), info.RawDbiModuleIndex);
            Assert.Equal((0x23456789, 0x2000), info.RuntimePEIIndex);
            Assert.Equal((0x3456789A, 0x3000), info.DacPEIndex);
            Assert.Equal((0x456789AB, 0x4000), info.DbiPEIndex);
            Assert.Equal(header.AsSpan(25, 8).ToArray(), info.RuntimeBuildId);
            Assert.Equal(header.AsSpan(49, 8).ToArray(), info.DacBuildId);
            Assert.Equal(header.AsSpan(73, 8).ToArray(), info.DbiBuildId);
            Version expectedVersion = headerVersion >= 2 && headerSize == 112 ? new Version(11, 2, 3, 4) : null;
            Assert.True(info.TryReadVersion(target.Services, app.Exports["DotNetRuntimeInfo"], out Version runtimeVersion));
            Assert.Equal(expectedVersion, runtimeVersion);
            ClrInfo clrInfo = Assert.Single(target.Enumerate(RuntimeEnumerationFlags.All)).Services.GetService<ClrInfo>();
            Assert.Equal(expectedVersion ?? new Version(), clrInfo.Version);
            target.Symbols.Verify();
        }

        [Fact]
        public void SharedRuntimeInfoParserRejectsTruncatedHeader()
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, "/app/singlefile");
            target.AddSingleFileRuntime(app, headerSize: 95);

            Assert.False(RuntimeInfo.TryRead(target.Services, app.Exports["DotNetRuntimeInfo"], out RuntimeInfo info));
            Assert.False(info.IsValid);
            Assert.Empty(target.Enumerate(RuntimeEnumerationFlags.All));
            target.Symbols.Verify();
        }

        [Theory]
        [InlineData(99)]
        [InlineData(103)]
        [InlineData(107)]
        [InlineData(111)]
        public void SharedRuntimeInfoParserRejectsInvalidVersionWithoutThrowing(int offset)
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, "/app/singlefile");
            byte[] header = target.AddSingleFileRuntime(app);
            header[offset] = 128;

            Assert.True(RuntimeInfo.TryRead(target.Services, app.Exports["DotNetRuntimeInfo"], out RuntimeInfo info));
            Assert.True(info.IsValid);
            Assert.False(info.TryReadVersion(target.Services, app.Exports["DotNetRuntimeInfo"], out Version runtimeVersion));
            Assert.Null(runtimeVersion);
            Assert.Empty(target.Enumerate(RuntimeEnumerationFlags.All));
            target.Symbols.Verify();
        }

        [Fact]
        public void SharedRuntimeInfoParserBoundsDiagnosticBuildIds()
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, "/app/singlefile");
            byte[] header = target.AddSingleFileRuntime(app);
            header[24] = byte.MaxValue;
            header[48] = byte.MaxValue;
            header[72] = byte.MaxValue;

            Assert.True(RuntimeInfo.TryRead(target.Services, app.Exports["DotNetRuntimeInfo"], out RuntimeInfo info));
            Assert.False(info.IsValid);
            Assert.Equal(header.AsSpan(25, 23).ToArray(), info.RuntimeBuildId);
            Assert.Equal(header.AsSpan(49, 23).ToArray(), info.DacBuildId);
            Assert.Equal(header.AsSpan(73, 23).ToArray(), info.DbiBuildId);
            Assert.Empty(target.Enumerate(RuntimeEnumerationFlags.All));
            target.Symbols.Verify();
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(20, 0)]
        [InlineData(24, 24)]
        [InlineData(48, 24)]
        [InlineData(72, 24)]
        public void InvalidSingleFileHeadersAreNotRegistered(int offset, byte value)
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, "/app/singlefile");
            byte[] header = target.AddSingleFileRuntime(app);
            header[offset] = value;

            Assert.Empty(target.Enumerate(RuntimeEnumerationFlags.All));
            target.Symbols.Verify();
        }

        [Theory]
        [MemberData(nameof(Targets))]
        public void AllIncludesNonCoreClrModulesFromRegisteredProviders(string platform, Architecture architecture)
        {
            OSPlatform os = OSPlatform.Create(platform);
            ITarget factoryTarget = null;
            List<ulong> visited = [];
            string cdacPath = Path.Combine(AppContext.BaseDirectory, "private-cdac");
            using RuntimeDiscoveryTestTarget target = new(os, architecture, manager =>
                manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), services => {
                    factoryTarget = services.GetService<ITarget>();
                    return new TestRuntimeDiscoveryProvider(module => {
                        visited.Add(module.ImageBase);
                        if (module.ImageBase != 0x30000)
                        {
                            return null;
                        }
                        return new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(1, 2, 3, 4))
                        {
                            ContractDescriptorAddress = module.ImageBase + 0x100,
                            DebuggingLibraries = [RuntimeDebuggingLibrary.CreateWithBuildId(
                                RuntimeDebuggingLibraryKind.CDac, cdacPath, RuntimeInformation.ProcessArchitecture,
                                OSPlatform.Windows, RuntimeDebuggingLibraryIndex.None, ImmutableArray<byte>.Empty)],
                        };
                    }, priority: 1);
                }));
            target.AddCoreRuntime(0x20000);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x30000,
                os == OSPlatform.Windows ? @"C:\plugins\custom.dll" : "/plugins/custom");
            target.AddModule(0x40000, ApplicationName(os, "unrelated"), needsSymbols: true);
            target.Symbols.ExpectSymbolFile(0x40000, result: null);

            IRuntime[] runtimes = target.Enumerate(RuntimeEnumerationFlags.All);
            Assert.Equal(2, runtimes.Length);
            Assert.Contains(runtimes, runtime => runtime.RuntimeType == RuntimeType.NetCore);
            IRuntime custom = Assert.Single(runtimes, runtime => runtime.RuntimeType == RuntimeType.NativeAOT);
            Assert.Equal(app.ImageBase, custom.RuntimeModule.ImageBase);
            Assert.Equal(new Version(1, 2, 3, 4), custom.RuntimeVersion);
            Assert.Same(custom.Target, factoryTarget);
            ClrInfo info = custom.Services.GetService<ClrInfo>();
            Assert.Equal(app.ImageBase + 0x100, info.ContractDescriptorAddress);
            DebugLibraryInfo library = Assert.Single(info.DebuggingLibraries);
            Assert.Equal(DebugLibraryKind.CDac, library.Kind);
            Assert.Equal(cdacPath, library.FileName);
            Assert.Equal([0x20000UL, 0x30000UL, 0x40000UL], visited);
            int probes = target.ExportProbes.Count;
            Assert.Same(custom, Assert.Single(target.Enumerate(RuntimeEnumerationFlags.All), runtime => runtime.RuntimeType == RuntimeType.NativeAOT));
            Assert.Equal(probes, target.ExportProbes.Count);
            Assert.Equal([0x20000UL, 0x30000UL, 0x40000UL], visited);
            target.Symbols.Verify();
        }

        [Fact]
        public void DescriptionsFromAnotherTargetAreRejectedEvenWithTheSameModuleBase()
        {
            using RuntimeDiscoveryTestTarget other = new(OSPlatform.Linux, Architecture.X64);
            other.AddCoreRuntime(0x20000);
            IModule otherModule = Assert.Single(other.Enumerate()).RuntimeModule;
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64,
                manager => manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module =>
                        new RuntimeDescription(otherModule, RuntimeType.NativeAOT, new Version(1, 0)), priority: 1)));
            target.AddCoreRuntime(0x20000);

            DiagnosticsException exception = Assert.Throws<DiagnosticsException>(() => target.Enumerate());
            Assert.Contains("does not match the probed target module", exception.Message);
            target.Symbols.Verify();
            other.Symbols.Verify();
        }

        [Fact]
        public void SharedTargetRemainsUsableUntilLastRuntimeIsDisposed()
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64);
            target.AddCoreRuntime(0x20000);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x30000, "/app/singlefile");
            target.AddSingleFileRuntime(app);

            IRuntime[] runtimes = target.Enumerate(RuntimeEnumerationFlags.All);
            Assert.Equal(2, runtimes.Length);
            DataTarget shared = runtimes[0].Services.GetService<ClrInfo>().DataTarget;
            Assert.Same(shared, runtimes[1].Services.GetService<ClrInfo>().DataTarget);

            ((IDisposable)runtimes[0]).Dispose();
            ((IDisposable)runtimes[0]).Dispose();
            Assert.Equal(2, shared.EnumerateModules().Count());

            ((IDisposable)runtimes[1]).Dispose();
            Assert.Throws<ObjectDisposedException>(() => shared.EnumerateModules());
            target.Symbols.Verify();
        }

        [Fact]
        public void HigherPriorityProvidersReplaceMetadataForTheSameModule()
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Windows, Architecture.X64,
                manager => manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module =>
                        new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(1, 2, 3, 4)), priority: 1)));
            target.AddCoreRuntime(0x20000);

            IRuntime runtime = Assert.Single(target.Enumerate());
            Assert.Equal(RuntimeType.NativeAOT, runtime.RuntimeType);
            ClrInfo info = runtime.Services.GetService<ClrInfo>();
            Assert.Same(info, Assert.Single(info.DataTarget.ClrVersions));
            target.Symbols.Verify();
        }

        [Theory]
        [InlineData("LINUX", RuntimeEnumerationFlags.Default)]
        [InlineData("LINUX", RuntimeEnumerationFlags.All)]
        [InlineData("OSX", RuntimeEnumerationFlags.Default)]
        [InlineData("OSX", RuntimeEnumerationFlags.All)]
        public void CustomRuntimeHintIsRecognizedBeforeBroadeningDiscovery(string platform, RuntimeEnumerationFlags flags)
        {
            List<ulong> visited = [];
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Create(platform), Architecture.X64,
                manager => manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        visited.Add(module.ImageBase);
                        return module.ImageBase == 0x20000
                            ? new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(1, 0))
                            : null;
                    }, priority: 1)));
            target.AddModule(0x10000, "/lib/unrelated", needsSymbols: true);
            target.AddModule(0x20000, "/app/nativeaot");
            target.AddDiagnosticHeader(0x20000);
            if (flags == RuntimeEnumerationFlags.All)
            {
                target.Symbols.ExpectSymbolFile(0x10000, result: null);
            }

            IRuntime runtime = Assert.Single(target.Enumerate(flags));
            Assert.Equal(RuntimeType.NativeAOT, runtime.RuntimeType);
            Assert.Equal(flags == RuntimeEnumerationFlags.All ? [0x20000UL, 0x10000UL] : new[] { 0x20000UL }, visited);
            if (flags == RuntimeEnumerationFlags.Default)
            {
                Assert.DoesNotContain(target.ExportProbes, probe => probe.ModuleBase == 0x10000);
            }
            target.Symbols.Verify();
        }

        [Theory]
        [MemberData(nameof(Targets))]
        public void DefaultFallbackUsesRegisteredProvidersBeforeProbingSymbols(string platform, Architecture architecture)
        {
            OSPlatform os = OSPlatform.Create(platform);
            using RuntimeDiscoveryTestTarget target = new(os, architecture,
                manager => manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module =>
                        new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(1, 2)), priority: 1)));
            target.AddModule(0x20000, os == OSPlatform.Windows ? @"C:\plugins\custom.dll" : "/plugins/custom", needsSymbols: true);
            target.AddModule(0x30000, ApplicationName(os, "unrelated"), needsSymbols: true);

            IRuntime runtime = Assert.Single(target.Enumerate());
            Assert.Equal(RuntimeType.NativeAOT, runtime.RuntimeType);
            Assert.Equal(new Version(1, 2), runtime.RuntimeVersion);
            Assert.False(runtime.Services.GetService<ClrInfo>().IsSingleFile);
            Assert.Empty(target.ExportProbes);
            target.Symbols.Verify();
        }

        [Fact]
        public void ProviderCannotDescribeAnotherModuleInTheSameTarget()
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64,
                manager => manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), services =>
                    new TestRuntimeDiscoveryProvider(_ =>
                        new RuntimeDescription(services.GetService<IModuleService>().GetModuleFromBaseAddress(0x30000),
                            RuntimeType.NativeAOT, new Version(1, 0)), priority: 1)));
            target.AddCoreRuntime(0x20000);
            target.AddModule(0x30000, "/app/nativeaot");

            DiagnosticsException exception = Assert.Throws<DiagnosticsException>(() => target.Enumerate());
            Assert.Contains("does not match the probed target module", exception.Message);
            target.Symbols.Verify();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ProviderPriorityOverridesRegistrationOrder(bool highPriorityRegisteredFirst)
        {
            List<int> calls = [];
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64, manager => {
                int[] priorities = highPriorityRegisteredFirst ? [100, 50] : [50, 100];
                foreach (int priority in priorities)
                {
                    manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                        new TestRuntimeDiscoveryProvider(module => {
                            calls.Add(priority);
                            return new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(priority, 0));
                        }, priority));
                }
            });
            target.AddModule(0x20000, "/plugins/custom", needsSymbols: true);

            Assert.Equal(new Version(100, 0), Assert.Single(target.Enumerate()).RuntimeVersion);
            Assert.Equal([100], calls);
            Assert.Empty(target.ExportProbes);
            target.Symbols.Verify();
        }

        [Fact]
        public void HigherPriorityProviderCanDeclineRecognition()
        {
            List<int> calls = [];
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64, manager => {
                manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        calls.Add(100);
                        return null;
                    }, priority: 100));
                manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        calls.Add(50);
                        return new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(1, 0));
                    }, priority: 50));
            });
            target.AddModule(0x20000, "/plugins/custom", needsSymbols: true);

            Assert.Equal(RuntimeType.NativeAOT, Assert.Single(target.Enumerate()).RuntimeType);
            Assert.Equal([100, 50], calls);
            Assert.Empty(target.ExportProbes);
            target.Symbols.Verify();
        }

        [Fact]
        public void FallbackTriesEachProviderAcrossAllModules()
        {
            List<(int Priority, ulong ModuleBase)> calls = [];
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64, manager => {
                manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        calls.Add((100, module.ImageBase));
                        return null;
                    }, priority: 100));
                manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        calls.Add((50, module.ImageBase));
                        return module.ImageBase == 0x20000
                            ? new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(1, 0))
                            : null;
                    }, priority: 50));
            });
            target.AddModule(0x10000, "/plugins/first");
            target.AddModule(0x20000, "/plugins/second");

            Assert.Equal(0x20000UL, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal([(100, 0x10000UL), (100, 0x20000UL), (50, 0x10000UL), (50, 0x20000UL)], calls);
            target.Symbols.Verify();
        }

        [Fact]
        public void PriorityModulesTryAllProvidersBeforeFallback()
        {
            List<(int Priority, ulong ModuleBase)> calls = [];
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64, manager => {
                manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        calls.Add((100, module.ImageBase));
                        return null;
                    }, priority: 100));
                manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        calls.Add((50, module.ImageBase));
                        return module.ImageBase == 0x20000
                            ? new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(1, 0))
                            : null;
                    }, priority: 50));
            });
            target.AddModule(0x10000, "/plugins/fallback");
            target.AddModule(0x20000, "/plugins/hinted");
            target.AddDiagnosticHeader(0x20000);

            Assert.Equal(0x20000UL, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal([(100, 0x20000UL), (50, 0x20000UL)], calls);
            target.Symbols.Verify();
        }

        [Theory]
        [InlineData(RuntimeEnumerationFlags.Default)]
        [InlineData(RuntimeEnumerationFlags.All)]
        public void CrashRuntimePrecedesDiagnosticHint(RuntimeEnumerationFlags flags)
        {
            List<ulong> visited = [];
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64,
                manager => manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        visited.Add(module.ImageBase);
                        return module.ImageBase == 0x10000
                            ? null
                            : new RuntimeDescription(module, RuntimeType.NativeAOT, new Version(1, 0));
                    }, priority: 1)));
            target.AddModule(0x10000, "/lib/unrelated", needsSymbols: true);
            target.AddModule(0x20000, "/app/hinted");
            target.AddModule(0x30000, "/app/crashing");
            target.AddCrashInfo(0x30000);
            target.AddDiagnosticHeader(0x20000);
            if (flags == RuntimeEnumerationFlags.All)
            {
                target.Symbols.ExpectSymbolFile(0x10000, result: null);
            }

            IRuntime[] runtimes = target.Enumerate(flags);
            Assert.Equal(flags == RuntimeEnumerationFlags.All ? [0x30000UL, 0x20000UL] : new[] { 0x30000UL },
                runtimes.Select(runtime => runtime.RuntimeModule.ImageBase));
            Assert.Equal(flags == RuntimeEnumerationFlags.All ? [0x30000UL, 0x20000UL, 0x10000UL] : new[] { 0x30000UL }, visited);
            target.Symbols.Verify();
        }

        [Fact]
        public void CrashAndDiagnosticHintsDoNotProbeTheSameModuleTwice()
        {
            List<ulong> visited = [];
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64,
                manager => manager.AddProviderFactory(typeof(IRuntimeDescriptionProvider), _ =>
                    new TestRuntimeDiscoveryProvider(module => {
                        visited.Add(module.ImageBase);
                        return null;
                    }, priority: 1)));
            target.AddModule(0x10000, "/lib/unrelated", needsSymbols: true);
            target.AddCoreRuntime(0x20000);
            target.AddCrashInfo(0x20000);
            target.Symbols.ExpectSymbolFile(0x10000, result: null);

            Assert.Equal(0x20000UL, Assert.Single(target.Enumerate(RuntimeEnumerationFlags.All)).RuntimeModule.ImageBase);
            Assert.Equal([0x20000UL, 0x10000UL], visited);
            target.Symbols.Verify();
        }

        [Fact]
        public void UnrecognizedCrashRuntimeFallsBackToDiagnosticHint()
        {
            using RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64);
            target.AddModule(0x10000, "/lib/unrelated", needsSymbols: true);
            RuntimeDiscoveryTestTarget.ModuleDefinition app = target.AddModule(0x20000, "/app/singlefile");
            target.AddSingleFileRuntime(app);
            target.AddDiagnosticHeader(app.ImageBase);
            target.AddModule(0x30000, "/app/crashing", needsSymbols: true);
            target.AddCrashInfo(0x30000);
            target.Symbols.ExpectSymbolFile(0x30000, result: null);

            Assert.Equal(app.ImageBase, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal([0x30000UL, 0x20000UL], target.ExportProbes
                .Where(probe => probe.Symbol == "DotNetRuntimeInfo").Select(probe => probe.ModuleBase));
            target.Symbols.Verify();
        }

        private sealed class TestRuntimeDiscoveryProvider(Func<IModule, RuntimeDescription> describe, int priority = 0) : IRuntimeDescriptionProvider
        {
            public int Priority => priority;

            public bool TryDescribeRuntime(IModule module, out RuntimeDescription description)
            {
                description = describe(module);
                return description is not null;
            }
        }

        private static string ApplicationName(OSPlatform platform, string name) =>
            platform == OSPlatform.Windows ? $@"C:\app\{name}.exe" : $"/app/{name}";
    }
}
