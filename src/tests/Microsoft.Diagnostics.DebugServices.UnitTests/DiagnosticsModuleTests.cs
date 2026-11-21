// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using SOS.Extensions;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class DiagnosticsModuleTests
    {
        [Theory]
        [InlineData(nameof(ServiceManager.RegisterModule))]
        [InlineData(nameof(ServiceManager.RegisterCommands))]
        [InlineData(nameof(ServiceManager.FinalizeServices))]
        public void StartupRegistrationMethodsArePublic(string methodName)
        {
            MethodInfo method = typeof(ServiceManager).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(method);
            Assert.True(method.IsPublic);
        }

        [Fact]
        public void RegisteredModulesExposeSuccessfulRegistrationsInOrder()
        {
            ServiceManager manager = new();
            IServiceManager services = manager;
            Assert.Empty(services.DiagnosticModules);
            CoreModule core = new();
            TestModule additional = new();
            manager.RegisterModule(core);
            manager.RegisterModule(additional);
            manager.RegisterModule(new TestModule());
            Assert.Throws<InvalidOperationException>(() => manager.RegisterModule(new ThrowingModule()));

            Assert.Equal(new IDiagnosticsModule[] { core, additional }, services.DiagnosticModules);
        }

        [Fact]
        public void ExplicitModuleRegistrationRejectsNullAndLateRegistration()
        {
            ServiceManager manager = new();
            Assert.Equal("module", Assert.Throws<ArgumentNullException>(() => manager.RegisterModule(null)).ParamName);
            manager.FinalizeServices();

            Assert.Throws<InvalidOperationException>(() => manager.RegisterModule(new TestModule()));
        }

        [Fact]
        public void ExplicitModulesAreRegisteredOncePerType()
        {
            ServiceManager manager = new();
            TestModule module = new();
            manager.RegisterModule(module);
            manager.RegisterModule(new TestModule());
            manager.FinalizeServices();
            ServiceContainer services = manager.CreateServiceContainer(ServiceScope.Global, null);

            Assert.Same(module, services.GetRequiredService<ModuleValue>().ServiceModule);
            Assert.Equal(2, manager.EnumerateProviderFactories<IModuleProvider>().Count());
            services.DisposeServices();
        }

        [Fact]
        public void ExplicitModulesRegisterServicesProvidersAndCommands()
        {
            ServiceManager manager = new();
            manager.RegisterModule(new TestModule());
            manager.RegisterModule(new TestModule());
            manager.FinalizeServices();
            ServiceContainer services = manager.CreateServiceContainer(ServiceScope.Global, null);
            ModuleValue value = services.GetRequiredService<ModuleValue>();
            services.AddService<IConsoleService>(new CaptureConsoleService());
            CommandService commands = new();
            manager.RegisterCommands(commands);
            manager.RegisterCommands(commands);
            commands.Execute("module-probe", services);

            Assert.Same(value, services.GetRequiredService<ModuleValue>());
            Assert.Single(commands.Commands, command => command.name == "module-probe");
            Assert.Equal(1, value.Invocations);
            Assert.Same(value.ServiceModule, value.CommandModule);
            Assert.Equal(new[] { "first", "second" },
                manager.EnumerateProviderFactories<IModuleProvider>().Select(factory => factory(services).Name));
            services.DisposeServices();
            Assert.True(value.Disposed);
        }

        [Fact]
        public void ModuleInstancesArePerManager()
        {
            ServiceManager first = new();
            ServiceManager second = new();
            first.RegisterModule(new TestModule());
            second.RegisterModule(new TestModule());
            first.FinalizeServices();
            second.FinalizeServices();
            ServiceContainer firstServices = first.CreateServiceContainer(ServiceScope.Global, null);
            ServiceContainer secondServices = second.CreateServiceContainer(ServiceScope.Global, null);

            Assert.NotSame(firstServices.GetRequiredService<ModuleValue>(), secondServices.GetRequiredService<ModuleValue>());
            Assert.NotSame(firstServices.GetRequiredService<ModuleValue>().ServiceModule, secondServices.GetRequiredService<ModuleValue>().ServiceModule);
            firstServices.DisposeServices();
            secondServices.DisposeServices();
        }

        [Fact]
        public void HostRegistersOnlyCoreModuleByDefault()
        {
            Host host = new(HostType.DotnetDump);
            ServiceContainer services = host.CreateServiceContainer();

            Assert.IsType<CoreModule>(Assert.Single(host.ServiceManager.DiagnosticModules));
            Assert.Null(services.GetService<ModuleValue>());
            Assert.Empty(host.ServiceManager.EnumerateProviderFactories<IModuleProvider>());
            CommandService commands = new();
            host.ServiceManager.RegisterCommands(commands);
            Assert.Empty(commands.Commands);
            services.DisposeServices();
        }

        [Fact]
        public void HostRegistersExplicitModulesBeforeFinalization()
        {
            Host host = new(HostType.DotnetDump);
            host.ServiceManager.RegisterModule(new TestModule());
            ServiceContainer services = host.CreateServiceContainer();

            Assert.NotNull(services.GetRequiredService<ModuleValue>());
            Assert.Equal(2, host.ServiceManager.EnumerateProviderFactories<IModuleProvider>().Count());
            CommandService commands = new();
            host.ServiceManager.RegisterCommands(commands);
            Assert.Contains(commands.Commands, command => command.name == "module-probe");
            services.DisposeServices();
        }

        [Fact]
        public void HostInitializationRejectsInvalidAdditionalModules()
        {
            Assert.Equal("additionalModules", Assert.Throws<ArgumentNullException>(() =>
                HostServices.InitializeWithModules("unused", additionalModules: null)).ParamName);
            Assert.Equal("additionalModules", Assert.Throws<ArgumentException>(() =>
                HostServices.InitializeWithModules("unused", new IDiagnosticsModule[] { null })).ParamName);
        }

        [Fact]
        public void HostInitializationPreservesReflectionBoundSignatures()
        {
            MethodInfo initialize = Assert.Single(
                typeof(HostServices).GetMethods(BindingFlags.Public | BindingFlags.Static),
                method => method.Name == nameof(HostServices.Initialize));
            Assert.Equal(new[] { typeof(string) }, initialize.GetParameters().Select(parameter => parameter.ParameterType));
            Assert.NotNull(typeof(HostServices).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                new[] { typeof(string), typeof(IntPtr) },
                modifiers: null));
        }

        [Fact]
        public void ModuleRegistrationFailuresPropagate()
        {
            ServiceManager manager = new();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => manager.RegisterModule(new ThrowingModule()));

            Assert.Equal("Module registration failed", exception.Message);
        }

        [Fact]
        public void CommandRegistrationRequiresFinalizedServices()
        {
            ServiceManager manager = new();
            CommandService commands = new();

            Assert.Throws<InvalidOperationException>(() => manager.RegisterCommands(commands));
            Assert.Equal("commandService", Assert.Throws<ArgumentNullException>(() => manager.RegisterCommands(null)).ParamName);
        }

        [Fact]
        public void CommandRegistrationFailuresPropagate()
        {
            ServiceManager manager = new();
            manager.RegisterModule(new ThrowingCommandsModule());
            manager.FinalizeServices();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => manager.RegisterCommands(new CommandService()));

            Assert.Equal("Module command registration failed", exception.Message);
        }

        [Fact]
        public void ModulesRegisterCommandsForEachCommandService()
        {
            ServiceManager manager = new();
            manager.RegisterModule(new TestModule());
            manager.FinalizeServices();
            CommandService first = new();
            CommandService second = new();

            manager.RegisterCommands(first);
            manager.RegisterCommands(second);

            Assert.Single(first.Commands);
            Assert.Single(second.Commands);
        }

        public sealed class TestModule : IDiagnosticsModule
        {
            public void RegisterServices(IServiceRegistry registry)
            {
                registry.AddServiceFactory<ModuleValue>(ServiceScope.Global, _ => new ModuleValue(this));
                registry.AddProviderFactory<IModuleProvider>(_ => new ModuleProvider("first"));
                registry.AddProviderFactory<IModuleProvider>(_ => new ModuleProvider("second"));
            }

            public void RegisterCommands(ICommandService commandService) =>
                commandService.AddCommands<ModuleCommand>(services => new ModuleCommand(services.GetRequiredService<ModuleValue>(), this));
        }

        public sealed class ModuleValue : IDisposable
        {
            public ModuleValue(IDiagnosticsModule module) => ServiceModule = module;

            public IDiagnosticsModule ServiceModule { get; }
            public IDiagnosticsModule CommandModule { get; set; }
            public int Invocations { get; set; }
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        [Command(Name = "module-probe")]
        public sealed class ModuleCommand
        {
            private readonly ModuleValue _value;
            private readonly IDiagnosticsModule _module;

            public ModuleCommand(ModuleValue value, IDiagnosticsModule module)
            {
                _value = value;
                _module = module;
            }

            [CommandInvoke]
            public void Invoke()
            {
                _value.CommandModule = _module;
                _value.Invocations++;
            }
        }

        public interface IModuleProvider
        {
            string Name { get; }
        }

        private sealed class ModuleProvider : IModuleProvider
        {
            public ModuleProvider(string name) => Name = name;

            public string Name { get; }
        }

        public sealed class ThrowingModule : IDiagnosticsModule
        {
            public void RegisterServices(IServiceRegistry registry) => throw new InvalidOperationException("Module registration failed");
            public void RegisterCommands(ICommandService commandService) { }
        }

        public sealed class ThrowingCommandsModule : IDiagnosticsModule
        {
            public void RegisterServices(IServiceRegistry registry) { }
            public void RegisterCommands(ICommandService commandService) => throw new InvalidOperationException("Module command registration failed");
        }

    }
}
