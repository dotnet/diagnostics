// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.Runtime;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using SOS.Hosting;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class ServiceRegistryTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StatusCommandReportsRegisteredModulesWithoutChangingReset(bool reset)
        {
            Host host = new(HostType.DotnetDump);
            host.ServiceManager.RegisterModule(new SOSHostingModule());
            host.ServiceManager.RegisterModule(new AnalysisModule());
            ServiceContainer services = host.CreateServiceContainer();
            CaptureConsoleService console = new();
            services.AddService<IConsoleService>(console);
            services.AddService<ISymbolService>(new SymbolService(host));
            ContextService context = new(host);
            services.AddService<IContextService>(context);
            TestTarget target = new(host);
            context.SetCurrentTarget(target);
            CommandService commands = new();
            commands.AddCommands<StatusCommand>();

            commands.Execute(reset ? "sosflush" : "sosstatus", target.Services);

            if (reset)
            {
                Assert.Contains("Internal cached state reset", console.OutputLines);
                Assert.DoesNotContain("Diagnostics modules registered:", console.OutputLines);
            }
            else
            {
                Assert.Contains("Diagnostics modules registered:", console.OutputLines);
                string[] modules = console.OutputLines
                    .SkipWhile(line => line != "Diagnostics modules registered:")
                    .Skip(1)
                    .TakeWhile(line => line.StartsWith("-> ", StringComparison.Ordinal))
                    .ToArray();
                Assert.Equal(host.ServiceManager.DiagnosticModules.Select(module => {
                    Type type = module.GetType();
                    return $"-> {type.FullName} ({type.Assembly.GetName().Version})";
                }), modules);
                Assert.Contains(modules, line => line.Contains(typeof(CoreModule).FullName));
                Assert.Contains(modules, line => line.Contains(typeof(SOSHostingModule).FullName));
                Assert.Contains(modules, line => line.Contains(typeof(AnalysisModule).FullName));
                Assert.Contains(modules, line => line.Contains(typeof(DiagnosticsModuleTests.TestModule).FullName));
            }
            target.Destroy();
            services.DisposeServices();
        }

        [Fact]
        public void CoreServicesRegisterExpectedFactories()
        {
            RecordingRegistry registry = new();
            new CoreModule().RegisterServices(registry);

            Assert.Equal(8, registry.Services.Count);
            Assert.Contains((ServiceScope.Target, typeof(IDataReader)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Target, typeof(IClrSymbolProvider)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Target, typeof(IRuntimeService)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Module, typeof(PEModule)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Module, typeof(ELFModule)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Module, typeof(MachOModule)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Module, typeof(ELFFile)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Module, typeof(MachOFile)), registry.Services.Keys);
            Assert.Equal(typeof(IRuntimeProvider), Assert.Single(registry.Providers).Type);
        }

        [Fact]
        public void CoreServicesRejectNullRegistry()
        {
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => new CoreModule().RegisterServices(null)).ParamName);
        }

        [Fact]
        public void HostingAndAnalysisServicesRegisterExpectedFactories()
        {
            RecordingRegistry registry = new();
            new CoreModule().RegisterServices(registry);
            new SOSHostingModule().RegisterServices(registry);
            new AnalysisModule().RegisterServices(registry);

            Assert.Equal(22, registry.Services.Count);
            Assert.Contains((ServiceScope.Global, typeof(IHostAssetResolver)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Global, typeof(IClrDataProcessActivator)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Global, typeof(SOSLibrary)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Target, typeof(SOSHost)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Target, typeof(TargetWrapper)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Runtime, typeof(RuntimeWrapper)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Target, typeof(NativeAddressHelper)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Runtime, typeof(ClrMDHelper)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Runtime, typeof(DumpHeapService)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Runtime, typeof(GCHistory)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Runtime, typeof(ManagedFileLineService)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Runtime, typeof(LiveObjectService)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Runtime, typeof(StaticVariableService)), registry.Services.Keys);
            Assert.Contains((ServiceScope.Runtime, typeof(RootCacheService)), registry.Services.Keys);
            Assert.Single(registry.Providers);
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => new SOSHostingModule().RegisterServices(null)).ParamName);
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => new AnalysisModule().RegisterServices(null)).ParamName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("native-sos-directory")]
        public void HostAssetResolverUsesHostPathOrPackageFallback(string sosPath)
        {
            ServiceContainer empty = new(null);
            HostAssetResolver fallback = new(empty);
            ServiceManager manager = new();
            manager.RegisterModule(new SOSHostingModule());
            manager.FinalizeServices();
            ServiceContainer services = manager.CreateServiceContainer(ServiceScope.Global, null);
            services.AddService<SOSLibrary.ISOSModule>(new TestSOSModule(sosPath));

            IHostAssetResolver resolver = services.GetRequiredService<IHostAssetResolver>();
            Assert.Equal(string.IsNullOrEmpty(sosPath) ? fallback.NativeBinariesDirectory : sosPath, resolver.NativeBinariesDirectory);
            Assert.Same(resolver, services.GetRequiredService<IHostAssetResolver>());
            IClrDataProcessActivator activator = services.GetRequiredService<IClrDataProcessActivator>();
            Assert.IsType<ClrDataProcessActivator>(activator);
            Assert.Same(activator, services.GetRequiredService<IClrDataProcessActivator>());
            Assert.Equal(System.IO.Path.Combine(resolver.NativeBinariesDirectory, System.IO.Path.GetFileName(fallback.GetCDacPath())),
                resolver.GetCDacPath());
            services.DisposeServices();
            Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => new HostAssetResolver(null)).ParamName);
        }

        [Fact]
        public void SOSLibraryInitializationFailurePropagatesAndCanBeRetriedWithHostModule()
        {
            Host host = new(HostType.DotnetDump);
            host.ServiceManager.RegisterModule(new SOSHostingModule());
            ServiceContainer services = host.CreateServiceContainer();
            services.AddService<IHostAssetResolver>(new TestHostAssetResolver());

            Assert.Throws<ArgumentNullException>(() => services.GetRequiredService<SOSLibrary>());

            // A borrowed handle skips library loading and must not be freed by this service.
            services.AddService<SOSLibrary.ISOSModule>(new TestSOSModule("host-sos-directory", new IntPtr(1)));
            SOSLibrary library = services.GetRequiredService<SOSLibrary>();
            Assert.Equal("host-sos-directory", library.SOSPath);
            Assert.Same(library, services.GetRequiredService<SOSLibrary>());
            services.DisposeServices();
        }

        [Fact]
        public void OptionalAnalysisServicesCanBeUnavailable()
        {
            Host host = new(HostType.DotnetDump);
            host.ServiceManager.RegisterModule(new AnalysisModule());
            host.CreateServiceContainer();
            TestTarget target = new(host);
            ServiceContainer runtime = host.ServiceManager.CreateServiceContainer(ServiceScope.Runtime, target.Services);

            Assert.Null(target.Services.GetService<NativeAddressHelper>());
            Assert.Null(runtime.GetService<ClrMDHelper>());
            target.Destroy();
            host.ServiceContainer.DisposeServices();
        }

        [Fact]
        public void ClrMDFactoryResolvesOptionalRuntime()
        {
            Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => ClrMDHelper.TryCreate(null)).ParamName);
            ServiceContainer services = new(null);
            Assert.Null(ClrMDHelper.TryCreate(services));
        }

        [Fact]
        public void NativeAddressFactoryResolvesDependenciesOnlyWhenSupported()
        {
            Assert.Equal("services", Assert.Throws<ArgumentNullException>(() => NativeAddressHelper.TryCreate(null)).ParamName);

            Host host = new(HostType.DotnetDump);
            host.CreateServiceContainer();
            TestTarget target = new(host);
            ServiceContainer services = new(null);
            services.AddService<ITarget>(target);
            Assert.Null(NativeAddressHelper.TryCreate(services));

            services.AddService<IMemoryRegionService>(new TestMemoryRegionService());
            DiagnosticsException exception = Assert.Throws<DiagnosticsException>(() => NativeAddressHelper.TryCreate(services));
            Assert.Contains(nameof(IMemoryService), exception.Message);
            target.Destroy();
            host.ServiceContainer.DisposeServices();
        }

        [Fact]
        public void AnalysisFactorySuppliesDependenciesDirectly()
        {
            Host host = new(HostType.DotnetDump);
            host.ServiceManager.RegisterModule(new AnalysisModule());
            host.CreateServiceContainer();
            CaptureConsoleService console = new();
            host.ServiceContainer.AddService<IConsoleService>(console);
            TestTarget target = new(host);
            TestMemoryRegionService regions = new();
            ((ServiceContainer)target.Services).AddService<IMemoryRegionService>(regions);

            NativeAddressHelper helper = target.Services.GetRequiredService<NativeAddressHelper>();
            Assert.Same(target, helper.Target);
            Assert.Same(regions, helper.MemoryRegionService);
            Assert.Same(target.Services.GetRequiredService<IMemoryService>(), helper.MemoryService);
            Assert.Same(target.Threads, helper.ThreadService);
            Assert.Same(target.Modules, helper.ModuleService);
            Assert.Same(target.Services.GetRequiredService<IRuntimeService>(), helper.RuntimeService);
            Assert.Same(console, helper.Console);
            Assert.Same(helper, target.Services.GetRequiredService<NativeAddressHelper>());

            target.Flush();
            target.Destroy();
            host.ServiceContainer.DisposeServices();
        }

        [Fact]
        public void CoreDataReaderRequiresItsExplicitDependencies()
        {
            Host host = new(HostType.DotnetDump);
            host.CreateServiceContainer();
            TestTarget target = new(host, registerThreads: false);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => target.Services.GetRequiredService<IDataReader>());
            Assert.Contains(nameof(IThreadService), exception.Message);
            target.Destroy();
            host.ServiceContainer.DisposeServices();
        }

        [Theory]
        [InlineData(ServiceScope.Global)]
        [InlineData(ServiceScope.Context)]
        [InlineData(ServiceScope.Target)]
        [InlineData(ServiceScope.Module)]
        [InlineData(ServiceScope.Thread)]
        [InlineData(ServiceScope.Runtime)]
        public void FactoriesAreLazyAndCachedPerContainer(ServiceScope scope)
        {
            ServiceManager manager = new();
            IServiceRegistry registry = manager;
            int creations = 0;
            registry.AddServiceFactory<DisposableService>(scope, _ => {
                creations++;
                return new DisposableService();
            });
            manager.FinalizeServices();
            ServiceContainer first = manager.CreateServiceContainer(scope, null);
            ServiceContainer second = manager.CreateServiceContainer(scope, null);
            Assert.Equal(0, creations);

            DisposableService firstService = first.GetRequiredService<DisposableService>();
            Assert.Same(firstService, first.GetRequiredService<DisposableService>());
            Assert.NotSame(firstService, second.GetRequiredService<DisposableService>());
            Assert.Equal(2, creations);
            first.DisposeServices();
            second.DisposeServices();
        }

        [Fact]
        public void LocalServicesOverrideParentsWithoutOwningThem()
        {
            ServiceManager manager = new();
            IServiceRegistry registry = manager;
            registry.AddServiceFactory<DisposableService>(ServiceScope.Target, _ => new DisposableService());
            manager.FinalizeServices();

            DisposableService parentService = new();
            object shared = new();
            ServiceContainer parent = new(null);
            parent.AddService(parentService);
            parent.AddService(shared);
            ServiceContainer child = manager.CreateServiceContainer(ServiceScope.Target, parent);

            DisposableService localService = child.GetRequiredService<DisposableService>();
            Assert.NotSame(parentService, localService);
            Assert.Same(shared, child.GetRequiredService<object>());
            child.DisposeServices();
            Assert.Equal(1, localService.Disposals);
            Assert.Equal(0, parentService.Disposals);
            parent.DisposeServices();
            Assert.Equal(1, parentService.Disposals);
        }

        [Fact]
        public void InvalidatedServicesAreCreatedAgain()
        {
            ServiceManager manager = new();
            manager.AddServiceFactory<object>(ServiceScope.Context, _ => new object());
            manager.FinalizeServices();
            ServiceContainer container = manager.CreateServiceContainer(ServiceScope.Context, null);
            object first = container.GetRequiredService<object>();

            container.RemoveService(typeof(object));
            Assert.NotSame(first, container.GetRequiredService<object>());
        }

        [Fact]
        public void NullFactoriesAreCachedAndDoNotFallBackToParent()
        {
            ServiceManager manager = new();
            int creations = 0;
            manager.AddServiceFactory<object>(ServiceScope.Runtime, _ => {
                creations++;
                return null;
            });
            manager.FinalizeServices();
            ServiceContainer parent = new(null);
            parent.AddService(new object());
            ServiceContainer container = manager.CreateServiceContainer(ServiceScope.Runtime, parent);

            Assert.Null(container.GetService<object>());
            Assert.Null(container.GetService<object>());
            Assert.Equal(1, creations);
            Assert.Contains(nameof(Object), Assert.Throws<DiagnosticsException>(() => container.GetRequiredService<object>()).Message);
        }

        [Fact]
        public void ProvidersRemainLazyAndOrdered()
        {
            ServiceManager manager = new();
            IServiceRegistry registry = manager;
            object first = new();
            object second = new();
            int creations = 0;
            registry.AddProviderFactory<object>(_ => {
                creations++;
                return first;
            });
            registry.AddProviderFactory<object>(_ => {
                creations++;
                return second;
            });
            manager.FinalizeServices();

            Func<IServiceProvider, object>[] factories = manager.EnumerateProviderFactories<object>().ToArray();
            Assert.Equal(0, creations);
            Assert.Equal(2, factories.Length);
            ServiceContainer container = manager.CreateServiceContainer(ServiceScope.Target, null);
            Assert.Same(first, factories[0](container));
            Assert.Same(second, factories[1](container));
            Assert.Equal(2, creations);
        }

        [Fact]
        public void DuplicateServicesAndLateRegistrationAreRejected()
        {
            ServiceManager manager = new();
            IServiceRegistry registry = manager;
            registry.AddServiceFactory<object>(ServiceScope.Global, _ => new object());
            Assert.Throws<ArgumentException>(() => registry.AddServiceFactory<object>(ServiceScope.Global, _ => new object()));
            manager.FinalizeServices();
            Assert.Throws<InvalidOperationException>(() => registry.AddServiceFactory<string>(ServiceScope.Global, _ => ""));
            Assert.Throws<InvalidOperationException>(() => registry.AddProviderFactory<object>(_ => new object()));
        }

        [Fact]
        public void GenericProviderEnumerationKeepsTypesSeparateAndRequiresFinalization()
        {
            ServiceManager manager = new();
            IServiceManager services = manager;
            Assert.Throws<InvalidOperationException>(() => services.EnumerateProviderFactories<string>());
            manager.AddProviderFactory<string>(_ => "provider");
            Dependency dependency = new();
            manager.AddProviderFactory<Dependency>(_ => dependency);
            manager.FinalizeServices();

            Assert.Equal("provider", Assert.Single(services.EnumerateProviderFactories<string>())(null));
            Assert.Same(dependency, Assert.Single(services.EnumerateProviderFactories<Dependency>())(null));
            Assert.Empty(services.EnumerateProviderFactories<object>());
        }

        [Fact]
        public void GenericFactoriesRejectNullDelegates()
        {
            IServiceRegistry registry = new ServiceManager();
            Assert.Equal("factory", Assert.Throws<ArgumentNullException>(
                () => registry.AddServiceFactory<object>(ServiceScope.Target, null)).ParamName);
            Assert.Equal("factory", Assert.Throws<ArgumentNullException>(
                () => registry.AddProviderFactory<object>(null)).ParamName);
        }

        [Fact]
        public void GenericFactoriesPreserveTypedResultsAndServiceProvider()
        {
            ServiceManager manager = new();
            IServiceRegistry registry = manager;
            Dependency dependency = new();
            registry.AddServiceFactory<IConstructorService>(ServiceScope.Target, services => new ConstructorService(dependency, services));
            registry.AddProviderFactory<IConstructorService>(services => new ConstructorService(dependency, services));
            manager.FinalizeServices();
            ServiceContainer container = manager.CreateServiceContainer(ServiceScope.Target, null);

            ConstructorService service = Assert.IsType<ConstructorService>(container.GetRequiredService<IConstructorService>());
            ConstructorService provider = Assert.IsType<ConstructorService>(
                Assert.Single(manager.EnumerateProviderFactories<IConstructorService>())(container));
            Assert.Same(dependency, service.Dependency);
            Assert.Same(dependency, provider.Dependency);
            Assert.Same(container, service.Services);
            Assert.Same(container, provider.Services);
            provider.Dispose();
            container.DisposeServices();
            Assert.Equal(1, service.Disposals);
        }

        [Fact]
        public void RequiredServicesRejectMissingServicesAndNullProviders()
        {
            ServiceContainer container = new(null);
            Assert.Null(container.GetService<object>());
            Assert.Throws<DiagnosticsException>(() => container.GetRequiredService<object>());
            Assert.Equal("serviceProvider", Assert.Throws<ArgumentNullException>(
                () => ServiceProviderExtensions.GetRequiredService<object>(null)).ParamName);
        }

        [Fact]
        public void TypedRegistrationUsesConstructorInjectionAndContainerLifetime()
        {
            ServiceManager manager = new();
            IServiceRegistry registry = manager;
            ConstructorService.Creations = 0;
            registry.AddService<IConstructorService, ConstructorService>(ServiceScope.Target);
            registry.AddService<ConstructorService>(ServiceScope.Runtime);
            registry.AddProvider<IConstructorService, ConstructorService>();
            Assert.Equal(0, ConstructorService.Creations);
            manager.FinalizeServices();
            Dependency dependency = new();
            ServiceContainer parent = new(null);
            parent.AddService(dependency);
            ServiceContainer target = manager.CreateServiceContainer(ServiceScope.Target, parent);

            ConstructorService service = Assert.IsType<ConstructorService>(target.GetRequiredService<IConstructorService>());
            Assert.Same(dependency, service.Dependency);
            Assert.Same(target, service.Services);
            Assert.Same(service, target.GetRequiredService<IConstructorService>());
            Assert.Equal(17, service.OptionalValue);
            Assert.Equal(1, ConstructorService.Creations);

            ServiceContainer runtime = manager.CreateServiceContainer(ServiceScope.Runtime, target);
            ConstructorService runtimeService = runtime.GetRequiredService<ConstructorService>();
            Assert.NotSame(service, runtimeService);
            Assert.Same(dependency, runtimeService.Dependency);
            Assert.Same(runtime, runtimeService.Services);
            Assert.Equal(2, ConstructorService.Creations);
            Func<IServiceProvider, IConstructorService> providerFactory = Assert.Single(manager.EnumerateProviderFactories<IConstructorService>());
            ConstructorService provider = Assert.IsType<ConstructorService>(providerFactory(target));
            Assert.Equal(3, ConstructorService.Creations);
            Assert.Same(dependency, provider.Dependency);
            provider.Dispose();
            runtime.DisposeServices();
            target.DisposeServices();
            Assert.Equal(1, runtimeService.Disposals);
            Assert.Equal(1, service.Disposals);
        }

        [Fact]
        public void TypedRegistrationRejectsNullRegistry()
        {
            IServiceRegistry registry = null;
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(
                () => registry.AddService<IConstructorService, ConstructorService>(ServiceScope.Target)).ParamName);
            Assert.Equal("registry", Assert.Throws<ArgumentNullException>(
                () => registry.AddProvider<IConstructorService, ConstructorService>()).ParamName);
        }

        [Fact]
        public void CoreDataReaderUsesExplicitDependenciesAndFlushesModuleCache()
        {
            Host host = new(HostType.DotnetDump);
            host.CreateServiceContainer();
            TestTarget target = new(host);
            IDataReader reader = target.Services.GetRequiredService<IDataReader>();

            Assert.IsType<DataReader>(reader);
            Assert.Same(reader, target.Services.GetRequiredService<IDataReader>());
            Assert.Equal(8, reader.PointerSize);
            Assert.Equal(42, reader.ProcessId);
            Assert.Empty(reader.EnumerateModules());
            Assert.Empty(reader.EnumerateModules());
            Assert.Equal(1, target.Modules.Enumerations);
            target.Flush();
            Assert.Empty(reader.EnumerateModules());
            Assert.Equal(2, target.Modules.Enumerations);

            byte[] buffer = new byte[4];
            Assert.Equal(4, reader.Read(0, buffer));
            Assert.Equal(new byte[] { 42, 42, 42, 42 }, buffer);
            Assert.False(reader.GetThreadContext(1, 0, buffer));
            Assert.Equal(1, target.Threads.Lookups);

            Assert.IsType<HostSymbolProvider>(target.Services.GetRequiredService<IClrSymbolProvider>());
            Assert.IsType<RuntimeService>(target.Services.GetRequiredService<IRuntimeService>());
            Func<IServiceProvider, IRuntimeProvider> provider = Assert.Single(host.ServiceManager.EnumerateProviderFactories<IRuntimeProvider>());
            Assert.IsType<RuntimeProvider>(provider(target.Services));
            target.Destroy();
            host.ServiceContainer.DisposeServices();
        }

        private sealed class RecordingRegistry : IServiceRegistry
        {
            public Dictionary<(ServiceScope Scope, Type Type), ServiceFactory> Services { get; } = new();
            public List<(Type Type, ServiceFactory Factory)> Providers { get; } = new();

            public void AddServiceFactory<TService>(ServiceScope scope, Func<IServiceProvider, TService> factory) where TService : class =>
                Services.Add((scope, typeof(TService)), services => factory(services));

            public void AddProviderFactory<TProvider>(Func<IServiceProvider, TProvider> factory) where TProvider : class =>
                Providers.Add((typeof(TProvider), services => factory(services)));
        }

        private interface IConstructorService
        {
        }

        private sealed class Dependency
        {
        }

        private sealed class ConstructorService : IConstructorService, IDisposable
        {
            public static int Creations { get; set; }
            public Dependency Dependency { get; }
            public IServiceProvider Services { get; }
            public int OptionalValue { get; }
            public int Disposals { get; private set; }

            public ConstructorService(Dependency dependency, IServiceProvider services, int optionalValue = 17)
            {
                Creations++;
                Dependency = dependency;
                Services = services;
                OptionalValue = optionalValue;
            }

            public void Dispose() => Disposals++;
        }

        private sealed class DisposableService : IDisposable
        {
            public int Disposals { get; private set; }
            public void Dispose() => Disposals++;
        }

        private sealed class TestTarget : Target
        {
            public TestModuleService Modules { get; } = new();
            public TestThreadService Threads { get; } = new();

            public TestTarget(Host host, bool registerThreads = true) : base(host, null)
            {
                OperatingSystem = OSPlatform.Windows;
                Architecture = Architecture.X64;
                ProcessId = 42;
                _serviceContainerFactory.AddServiceFactory<IModuleService>(_ => Modules);
                if (registerThreads)
                {
                    _serviceContainerFactory.AddServiceFactory<IThreadService>(_ => Threads);
                }
                _serviceContainerFactory.AddServiceFactory<IMemoryService>(_ => new TestMemoryService());
                Finished();
            }
        }

        private sealed class TestSOSModule : SOSLibrary.ISOSModule
        {
            public TestSOSModule(string sosPath, IntPtr sosHandle = default)
            {
                SOSPath = sosPath;
                SOSHandle = sosHandle;
            }
            public string SOSPath { get; }
            public IntPtr SOSHandle { get; }
        }

        private sealed class TestHostAssetResolver : IHostAssetResolver
        {
            public string NativeBinariesDirectory => null;
            public string GetCDacPath() => throw new NotSupportedException();
        }

        private sealed class TestMemoryService : IMemoryService
        {
            public int PointerSize => 8;

            public bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead)
            {
                buffer.Fill(42);
                bytesRead = buffer.Length;
                return true;
            }

            public bool WriteMemory(ulong address, Span<byte> buffer, out int bytesWritten) =>
                throw new NotSupportedException();
        }

        private sealed class TestMemoryRegionService : IMemoryRegionService
        {
            public IEnumerable<IMemoryRegion> EnumerateRegions() => Array.Empty<IMemoryRegion>();
        }

        private sealed class TestModuleService : IModuleService
        {
            public int Enumerations { get; private set; }
            public IModule EntryPointModule => null;

            public IEnumerable<IModule> EnumerateModules()
            {
                Enumerations++;
                return Array.Empty<IModule>();
            }

            public IModule GetModuleFromIndex(int moduleIndex) => throw new NotSupportedException();
            public IModule GetModuleFromBaseAddress(ulong baseAddress) => throw new NotSupportedException();
            public IModule GetModuleFromAddress(ulong address) => throw new NotSupportedException();
            public IEnumerable<IModule> GetModuleFromModuleName(string moduleName) => throw new NotSupportedException();
            public IModule CreateModule(int moduleIndex, ulong imageBase, ulong imageSize, string imageName) => throw new NotSupportedException();
        }

        private sealed class TestThreadService : IThreadService
        {
            public int Lookups { get; private set; }
            public IEnumerable<RegisterInfo> Registers => Array.Empty<RegisterInfo>();
            public int InstructionPointerIndex => 0;
            public int FramePointerIndex => 0;
            public int StackPointerIndex => 0;
            public IEnumerable<IThread> EnumerateThreads() => Array.Empty<IThread>();
            public IThread GetThreadFromIndex(int threadIndex) => throw new NotSupportedException();

            public IThread GetThreadFromId(uint threadId)
            {
                Lookups++;
                throw new DiagnosticsException("No thread");
            }

            public bool TryGetRegisterIndexByName(string name, out int registerIndex) => throw new NotSupportedException();
            public bool TryGetRegisterInfo(int registerIndex, out RegisterInfo info) => throw new NotSupportedException();
        }
    }
}
