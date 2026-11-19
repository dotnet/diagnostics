// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Registers explicit lazy factories for scoped services and providers.
    /// </summary>
    public class ServiceManager : IServiceManager, IServiceRegistry
    {
        private readonly Dictionary<Type, ServiceFactory>[] _factories;
        private readonly Dictionary<Type, object> _providerFactories;
        private readonly HashSet<Func<IDiagnosticsModule>> _registeredModuleFactories = new();
        private readonly List<IDiagnosticsModule> _modules = new();
        private readonly HashSet<ICommandService> _registeredCommandServices = new();
        private bool _finalized;

        /// <summary>
        /// Create a service manager instance.
        /// </summary>
        public ServiceManager()
        {
            _factories = new Dictionary<Type, ServiceFactory>[(int)ServiceScope.Max];
            _providerFactories = new Dictionary<Type, object>();
            for (int i = 0; i < (int)ServiceScope.Max; i++)
            {
                _factories[i] = new Dictionary<Type, ServiceFactory>();
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<IDiagnosticsModule> DiagnosticModules => _modules.AsReadOnly();

        /// <summary>
        /// Registers a module's services once per module type and retains it for command registration.
        /// </summary>
        public void RegisterModule(IDiagnosticsModule module)
        {
            if (module is null)
            {
                throw new ArgumentNullException(nameof(module));
            }
            if (_finalized)
            {
                throw new InvalidOperationException();
            }
            if (_modules.Exists(registered => registered.GetType() == module.GetType()))
            {
                return;
            }
            module.RegisterServices(this);
            _modules.Add(module);
        }

        /// <summary>
        /// Creates a module per factory and registers its services once per service manager.
        /// </summary>
        public void RegisterModules(IEnumerable<Func<IDiagnosticsModule>> factories)
        {
            if (factories is null)
            {
                throw new ArgumentNullException(nameof(factories));
            }
            if (_finalized)
            {
                throw new InvalidOperationException();
            }
            foreach (Func<IDiagnosticsModule> factory in factories)
            {
                if (factory is null)
                {
                    throw new ArgumentException("Diagnostics module factories must not contain null.", nameof(factories));
                }
                if (_registeredModuleFactories.Contains(factory))
                {
                    continue;
                }
                IDiagnosticsModule module = factory() ?? throw new InvalidOperationException("A diagnostics module factory returned null.");
                RegisterModule(module);
                _registeredModuleFactories.Add(factory);
            }
        }

        /// <summary>
        /// Registers all modules' commands once per command service, after services are finalized.
        /// </summary>
        public void RegisterCommands(ICommandService commandService)
        {
            if (commandService is null)
            {
                throw new ArgumentNullException(nameof(commandService));
            }
            if (!_finalized)
            {
                throw new InvalidOperationException();
            }
            if (_registeredCommandServices.Contains(commandService))
            {
                return;
            }
            foreach (IDiagnosticsModule module in _modules)
            {
                module.RegisterCommands(commandService);
            }
            _registeredCommandServices.Add(commandService);
        }

        /// <summary>
        /// Creates a new service container factory with all the registered factories for the given scope.
        /// </summary>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="parent">parent service provider to chain to</param>
        /// <returns>The service container factory.</returns>
        public ServiceContainerFactory CreateServiceContainerFactory(ServiceScope scope, IServiceProvider parent)
        {
            if (!_finalized)
            {
                throw new InvalidOperationException();
            }
            return new ServiceContainerFactory(parent, _factories[(int)scope]);
        }

        /// <summary>
        /// Get the provider factories for a type or interface.
        /// </summary>
        /// <typeparam name="TProvider">provider type or interface</typeparam>
        /// <returns>The provider factories for the type.</returns>
        public IEnumerable<Func<IServiceProvider, TProvider>> EnumerateProviderFactories<TProvider>() where TProvider : class
        {
            if (!_finalized)
            {
                throw new InvalidOperationException();
            }
            if (_providerFactories.TryGetValue(typeof(TProvider), out object factories))
            {
                return (List<Func<IServiceProvider, TProvider>>)factories;
            }
            return Array.Empty<Func<IServiceProvider, TProvider>>();
        }

        /// <summary>
        /// Add a service factory for the specific scope.
        /// </summary>
        /// <typeparam name="TService">service type or interface</typeparam>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory<TService>(ServiceScope scope, Func<IServiceProvider, TService> factory) where TService : class
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            if (_finalized)
            {
                throw new InvalidOperationException();
            }
            _factories[(int)scope].Add(typeof(TService), services => factory(services));
        }

        /// <summary>
        /// Add a provider factory.
        /// </summary>
        /// <typeparam name="TProvider">provider type or interface</typeparam>
        /// <param name="factory">function to create provider instance</param>
        public void AddProviderFactory<TProvider>(Func<IServiceProvider, TProvider> factory) where TProvider : class
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            if (_finalized)
            {
                throw new InvalidOperationException();
            }
            if (!_providerFactories.TryGetValue(typeof(TProvider), out object factories))
            {
                _providerFactories.Add(typeof(TProvider), factories = new List<Func<IServiceProvider, TProvider>>());
            }
            ((List<Func<IServiceProvider, TProvider>>)factories).Add(factory);
        }

        /// <summary>
        /// Finalizes the service manager. Adding factories is not allowed after this call.
        /// </summary>
        public void FinalizeServices() => _finalized = true;
    }
}
