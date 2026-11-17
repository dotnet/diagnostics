// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class RuntimesCommandTests
    {
        [Theory]
        [InlineData("--module preferred")]
        [InlineData("--address 30000")]
        [InlineData("--address 0x30000")]
        [InlineData("--address 00000000`00030000")]
        public void PreferenceRefreshesCachedDiscoveryAndCanBeCleared(string option)
        {
            using RuntimeDiscoveryTestTarget target = CreateTarget();
            IRuntime original = Assert.Single(target.Enumerate());
            IContextService context = target.Services.GetService<IContextService>();
            Assert.Same(original, context.GetCurrentRuntime());
            (CommandService commands, ServiceContainer services, CaptureConsoleService console) = CreateCommandServices(target);

            commands.Execute($"runtimes {option}", services);

            Assert.Equal(0x30000UL, target.Runtimes.PreferredRuntimeModule);
            Assert.Equal(0x30000UL, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal(0x30000UL, context.GetCurrentRuntime().RuntimeModule.ImageBase);
            Assert.Contains(console.OutputLines, line => line.Contains("Preferred runtime module: 0000000000030000"));

            target.Flush();
            Assert.Equal(0x30000UL, target.Runtimes.PreferredRuntimeModule);
            Assert.Equal(0x30000UL, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            using RuntimeDiscoveryTestTarget other = CreateTarget();
            Assert.Null(other.Runtimes.PreferredRuntimeModule);
            Assert.Equal(0x20000UL, Assert.Single(other.Enumerate()).RuntimeModule.ImageBase);

            commands.Execute("runtimes --clear-priority --netcore", services);

            Assert.Null(target.Runtimes.PreferredRuntimeModule);
            Assert.Equal(0x20000UL, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal(0x20000UL, context.GetCurrentRuntime().RuntimeModule.ImageBase);
            target.Symbols.Verify();
            other.Symbols.Verify();
        }

        [Theory]
        [InlineData("--module missing", "was not found")]
        [InlineData("--address xyz", "Invalid module base address")]
        [InlineData("--address 20001", "Invalid module base address")]
        [InlineData("--module preferred --address 30000", "Specify only one")]
        [InlineData("--module preferred --clear-priority", "Specify only one")]
        [InlineData("--address 30000 --clear-priority", "Specify only one")]
        public void InvalidPreferenceDoesNotChangeCachedDiscovery(string option, string message)
        {
            using RuntimeDiscoveryTestTarget target = CreateTarget();
            target.Runtimes.PreferredRuntimeModule = 0x30000;
            IRuntime original = Assert.Single(target.Enumerate());
            (CommandService commands, ServiceContainer services, _) = CreateCommandServices(target);

            DiagnosticsException exception = Assert.Throws<DiagnosticsException>(() => commands.Execute($"runtimes {option}", services));

            Assert.Contains(message, exception.Message);
            Assert.Equal(0x30000UL, target.Runtimes.PreferredRuntimeModule);
            Assert.Same(original, Assert.Single(target.Enumerate()));
            target.Symbols.Verify();
        }

        [Fact]
        public void PreferredModuleOverridesNormalRuntimeDiscovery()
        {
            using RuntimeDiscoveryTestTarget target = CreateTarget();
            target.AddCoreRuntime(0x40000, diagnosticHeader: false);
            Assert.Equal(0x40000UL, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            (CommandService commands, ServiceContainer services, _) = CreateCommandServices(target);

            commands.Execute("runtimes --module preferred --netcore", services);

            Assert.Equal(0x30000UL, Assert.Single(target.Enumerate()).RuntimeModule.ImageBase);
            Assert.Equal(0x30000UL, target.Services.GetService<IContextService>().GetCurrentRuntime().RuntimeModule.ImageBase);
            target.Symbols.Verify();
        }

        [Fact]
        public void AllDoesNotPrioritizePreferredModule()
        {
            using RuntimeDiscoveryTestTarget target = CreateTarget();
            (CommandService commands, ServiceContainer services, _) = CreateCommandServices(target);

            commands.Execute("runtimes --module preferred --all", services);

            IRuntime[] runtimes = target.Enumerate();
            Assert.Equal(2, runtimes.Length);
            Assert.Equal(0x20000UL, runtimes[0].RuntimeModule.ImageBase);
            Assert.Equal(0x30000UL, runtimes[1].RuntimeModule.ImageBase);
            Assert.Equal(0x30000UL, target.Runtimes.PreferredRuntimeModule);
            Assert.Equal(0x20000UL, target.Services.GetService<IContextService>().GetCurrentRuntime().RuntimeModule.ImageBase);
            target.Symbols.Verify();
        }

        [Fact]
        public void AmbiguousModuleNameRequiresAnAddress()
        {
            using RuntimeDiscoveryTestTarget target = CreateTarget();
            target.AddModule(0x40000, "/other/preferred");
            (CommandService commands, ServiceContainer services, _) = CreateCommandServices(target);

            DiagnosticsException exception = Assert.Throws<DiagnosticsException>(() =>
                commands.Execute("runtimes --module preferred", services));

            Assert.Contains("ambiguous", exception.Message);
            Assert.Contains("--address", exception.Message);
            Assert.Null(target.Runtimes.PreferredRuntimeModule);
            target.Symbols.Verify();
        }

        private static RuntimeDiscoveryTestTarget CreateTarget()
        {
            RuntimeDiscoveryTestTarget target = new(OSPlatform.Linux, Architecture.X64);
            RuntimeDiscoveryTestTarget.ModuleDefinition automatic = target.AddModule(0x20000, "/app/automatic");
            RuntimeDiscoveryTestTarget.ModuleDefinition preferred = target.AddModule(0x30000, "/app/preferred");
            target.AddSingleFileRuntime(automatic);
            target.AddSingleFileRuntime(preferred);
            target.AddDiagnosticHeader(automatic.ImageBase);
            return target;
        }

        private static (CommandService Commands, ServiceContainer Services, CaptureConsoleService Console) CreateCommandServices(
            RuntimeDiscoveryTestTarget target)
        {
            CommandService commands = new();
            commands.AddCommands(typeof(RuntimesCommand));
            CaptureConsoleService console = new();
            ServiceContainer services = new(target.Services);
            services.AddService<IConsoleService>(console);
            return (commands, services, console);
        }
    }
}
