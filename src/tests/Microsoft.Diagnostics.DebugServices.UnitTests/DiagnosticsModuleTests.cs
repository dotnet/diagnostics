// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class DiagnosticsModuleTests
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            DiagnosticsModuleCatalog.Register(() => new TestModule());
            DiagnosticsModuleCatalog.Register(() => new BetaOrderedModule());
            DiagnosticsModuleCatalog.Register(() => new AlphaOrderedModule());
        }

        [Theory]
        [InlineData(nameof(ServiceManager.RegisterModule))]
        [InlineData(nameof(ServiceManager.RegisterModules))]
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
        public void ExplicitModulesAreNotRegisteredAgainFromTheCatalog()
        {
            ServiceManager manager = new();
            TestModule module = new();
            manager.RegisterModule(module);
            manager.RegisterModule(new TestModule());
            manager.RegisterModules(DiagnosticsModuleCatalog.GetFactories());
            manager.FinalizeServices();
            ServiceContainer services = manager.CreateServiceContainer(ServiceScope.Global, null);

            Assert.Same(module, services.GetRequiredService<ModuleValue>().ServiceModule);
            Assert.Equal(2, manager.EnumerateProviderFactories<IModuleProvider>().Count());
            services.DisposeServices();
        }

        [Fact]
        public void BuiltInModulesAreNotAdvertisedThroughInitializers()
        {
            IDiagnosticsModule[] modules = DiagnosticsModuleCatalog.GetFactories().Select(factory => factory()).ToArray();

            Assert.DoesNotContain(modules, module => module is CoreModule);
            Assert.DoesNotContain(modules, module => module is SOS.Hosting.SOSHostingModule);
            Assert.DoesNotContain(modules, module => module is Microsoft.Diagnostics.ExtensionCommands.AnalysisModule);
        }

        [Fact]
        public void ModuleInitializersRegisterServicesAndOrderedProvidersOnce()
        {
            ServiceManager manager = new();
            manager.RegisterModules(DiagnosticsModuleCatalog.GetFactories());
            manager.RegisterModules(DiagnosticsModuleCatalog.GetFactories());
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
            first.RegisterModules(DiagnosticsModuleCatalog.GetFactories());
            second.RegisterModules(DiagnosticsModuleCatalog.GetFactories());
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
        public void HostDoesNotRegisterCatalogAutomatically()
        {
            Host host = new(HostType.DotnetDump);
            ServiceContainer services = host.CreateServiceContainer();

            Assert.IsType<CoreModule>(Assert.Single(host.ServiceManager.DiagnosticModules));
            Assert.Null(services.GetService<ModuleValue>());
            Assert.Empty(host.ServiceManager.EnumerateProviderFactories<IModuleProvider>());
            CommandService commands = new();
            host.ServiceManager.RegisterCommands(commands);
            Assert.Empty(commands.Commands);
            Assert.Throws<InvalidOperationException>(() => host.ServiceManager.RegisterModules(DiagnosticsModuleCatalog.GetFactories()));
            services.DisposeServices();
        }

        [Fact]
        public void HostRegistersExplicitCatalogBeforeFinalization()
        {
            Host host = new(HostType.DotnetDump);
            host.ServiceManager.RegisterModules(DiagnosticsModuleCatalog.GetFactories());
            ServiceContainer services = host.CreateServiceContainer();

            Assert.NotNull(services.GetRequiredService<ModuleValue>());
            Assert.Equal(2, host.ServiceManager.EnumerateProviderFactories<IModuleProvider>().Count());
            CommandService commands = new();
            host.ServiceManager.RegisterCommands(commands);
            Assert.Contains(commands.Commands, command => command.name == "module-probe");
            Assert.Throws<InvalidOperationException>(() => host.ServiceManager.RegisterModules(DiagnosticsModuleCatalog.GetFactories()));
            services.DisposeServices();
        }

        [Fact]
        public void EmptyModuleListRegistersNoContributions()
        {
            ServiceManager manager = new();
            manager.RegisterModules(Array.Empty<Func<IDiagnosticsModule>>());
            manager.FinalizeServices();
            ServiceContainer services = manager.CreateServiceContainer(ServiceScope.Global, null);

            Assert.Null(services.GetService<ModuleValue>());
        }

        [Fact]
        public void NullFactoryListIsRejected()
        {
            Assert.Equal("factories", Assert.Throws<ArgumentNullException>(() => new ServiceManager().RegisterModules(null)).ParamName);
        }

        [Fact]
        public void NullFactoryIsRejected()
        {
            Assert.Equal("factories", Assert.Throws<ArgumentException>(() =>
                new ServiceManager().RegisterModules(new Func<IDiagnosticsModule>[] { null })).ParamName);
        }

        [Fact]
        public void NullModuleIsRejected()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                new ServiceManager().RegisterModules(new Func<IDiagnosticsModule>[] { () => null }));

            Assert.Equal("A diagnostics module factory returned null.", exception.Message);
        }

        [Fact]
        public void CatalogRejectsNullAndDuplicateFactories()
        {
            Assert.Equal("factory", Assert.Throws<ArgumentNullException>(() =>
                DiagnosticsModuleCatalog.Register<TestModule>(null)).ParamName);
            Assert.Throws<ArgumentException>(() => DiagnosticsModuleCatalog.Register(() => new TestModule()));
        }

        [Fact]
        public void CatalogOrderDoesNotDependOnInitializerOrder()
        {
            Type[] modules = DiagnosticsModuleCatalog.GetFactories().Select(factory => factory().GetType())
                .Where(type => type == typeof(AlphaOrderedModule) || type == typeof(BetaOrderedModule))
                .ToArray();

            Assert.Equal(new[] { typeof(AlphaOrderedModule), typeof(BetaOrderedModule) }, modules);
        }

        [Fact]
        public void ModuleFactoriesAreDeferredAndOnlyInvokedOncePerManager()
        {
            int creations = 0;
            Func<IDiagnosticsModule>[] factories = {
                () => {
                    creations++;
                    return PrivateConstructorModule.Create();
                }
            };
            ServiceManager manager = new();
            Assert.Equal(0, creations);

            manager.RegisterModules(factories);
            manager.RegisterModules(factories);

            Assert.Equal(1, creations);
        }

        [Fact]
        public void ModuleRegistrationFailuresPropagate()
        {
            ServiceManager manager = new();
            Func<IDiagnosticsModule>[] factories = { () => new ThrowingModule() };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => manager.RegisterModules(factories));

            Assert.Equal("Module registration failed", exception.Message);
        }

        [Fact]
        public void ModuleConstructorFailuresPropagate()
        {
            ServiceManager manager = new();
            Func<IDiagnosticsModule>[] factories = { () => new ThrowingConstructorModule() };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => manager.RegisterModules(factories));

            Assert.Equal("Module constructor failed", exception.Message);
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
            manager.RegisterModules(new Func<IDiagnosticsModule>[] { () => new ThrowingCommandsModule() });
            manager.FinalizeServices();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => manager.RegisterCommands(new CommandService()));

            Assert.Equal("Module command registration failed", exception.Message);
        }

        [Fact]
        public void ModulesRegisterCommandsForEachCommandService()
        {
            ServiceManager manager = new();
            manager.RegisterModules(new Func<IDiagnosticsModule>[] { () => new TestModule() });
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

        private abstract class EmptyModule : IDiagnosticsModule
        {
            public void RegisterServices(IServiceRegistry registry) { }
            public void RegisterCommands(ICommandService commandService) { }
        }

        private sealed class AlphaOrderedModule : EmptyModule
        {
        }

        private sealed class BetaOrderedModule : EmptyModule
        {
        }

        private sealed class PrivateConstructorModule : EmptyModule
        {
            private PrivateConstructorModule() { }

            public static PrivateConstructorModule Create() => new();
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

        public sealed class ThrowingConstructorModule : IDiagnosticsModule
        {
            public ThrowingConstructorModule() => throw new InvalidOperationException("Module constructor failed");

            public void RegisterServices(IServiceRegistry registry) { }
            public void RegisterCommands(ICommandService commandService) { }
        }

    }
}
