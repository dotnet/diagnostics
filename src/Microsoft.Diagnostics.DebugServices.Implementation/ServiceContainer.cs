// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// This service provider and container implementation caches the service instance. Calls
    /// the service factory for the type if not already instantiated and cached and if no 
    /// factory, chains to the parent service container.
    ///
    /// This implementations allows multiple instances of the same service type to be 
    /// registered. They are queried by getting the IEnumerable of the service type. If 
    /// the non-enumerable service type is queried and there are multiple instances, an 
    /// exception is thrown. The IRuntimeService implementation uses this feature to 
    /// enumerate all the IRuntimeProvider instances registered in the system.
    /// </summary>
    public class ServiceContainer : IServiceContainer
    {
        /// <summary>
        /// This wrapper class is used to shield this container from the IServiceProvider instance
        /// </summary>
        class ServiceProvider : IServiceProvider
        {
            private readonly ServiceContainer _container;

            internal ServiceProvider(ServiceContainer container)
            {
                _container = container;
            }

            public object GetService(Type type) => _container.GetService(type);
        }

        private readonly IServiceProvider _parent;
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<Type, object> _instances;
        private readonly Dictionary<Type, ServiceFactory> _factories;

        /// <summary>
        /// Create a service provider with parent provider and service factories
        /// </summary>
        /// <param name="parent">search this provider if service isn't found in this instance or null</param>
        /// <param name="factories">service factories to initialize provider or null</param>
        public ServiceContainer(IServiceProvider parent, IDictionary<Type, ServiceFactory> factories)
        {
            Debug.Assert(factories != null);
            _parent = parent;
            _serviceProvider = new ServiceProvider(this);
            _instances = new Dictionary<Type, object>();
            _factories = new Dictionary<Type, ServiceFactory>(factories);
        }

        /// <summary>
        /// Returns the IServiceProvider instance
        /// </summary>
        public IServiceProvider Services => _serviceProvider;

        /// <summary>
        /// Creates a copy container with the factories for the services and the parent
        /// in the container. Instantiated services are not transferred.
        /// </summary>
        /// <returns>clone</returns>
        public IServiceContainer Clone()
        {
            return new ServiceContainer(_parent, _factories);
        }

        /// <summary>
        /// Add a service factory. Multiple factories for the same type are allowed.
        /// </summary>
        /// <param name="type">service type or interface</param>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory(Type type, ServiceFactory factory)
        {
            if (type is null) throw new ArgumentNullException(nameof(type));
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            _factories.Add(type, factory);
        }

        /// <summary>
        /// Add a service instance. Multiple instances for the same type are not allowed.
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="service">service instance (must derives from type)</param>
        public void AddService(Type type, object service) => _instances.Add(type, service);

        /// <summary>
        /// Flushes the cached service instance for the specified type. Does not remove the service factory registered for the type.
        /// </summary>
        /// <param name="type">service type</param>
        public void RemoveService(Type type) => _instances.Remove(type);

        /// <summary>
        /// Flushes all the cached instances of the services. Does not remove any of the service factories registered.
        /// </summary>
        public void FlushServices() => _instances.Clear();

        /// <summary>
        /// Dispose of the instantiated services.
        /// </summary>
        public void DisposeServices()
        {
            foreach (object service in _instances.Values)
            {
                if (service is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _instances.Clear();
        }

        /// <summary>
        /// Get the cached/instantiated service instance if one exists. Don't call the factory or parent to create.
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="service">service instance (can be null)</param>
        /// <returns>if true, found service</returns>
        public bool TryGetCachedService(Type type, out object service)
        {
            Debug.Assert(type != null);
            if (type == typeof(IServiceProvider))
            {
                service = _serviceProvider;
                return true;
            }
            if (type == typeof(IServiceContainer))
            {
                service = this;
                return true;
            }
            return _instances.TryGetValue(type, out service);
        }

        /// <summary>
        /// Returns the instance of the service or returns null if service doesn't exist
        /// </summary>
        /// <param name="type">service type</param>
        /// <returns>service instance or null</returns>
        public object GetService(Type type)
        {
            if (TryGetCachedService(type, out object service))
            {
                return service;
            }
            if (_factories.TryGetValue(type, out ServiceFactory factory))
            {
                service = factory(_serviceProvider);
                _instances.Add(type, service);
            }
            else
            {
                service = _parent?.GetService(type);
            }
            return service;
        }
    }
}
