// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public class ServiceProvider : IServiceProvider
    {
        private readonly Func<IServiceProvider>[] _parents;
        private readonly Dictionary<Type, Func<object>> _factories;
        private readonly Dictionary<Type, object> _services;

        /// <summary>
        /// Create a service provider instance
        /// </summary>
        public ServiceProvider()
            : this(Array.Empty<Func<IServiceProvider>>())
        {
        }

        /// <summary>
        /// Create a service provider with parent provider
        /// </summary>
        /// <param name="parent">search this provider if service isn't found in this instance</param>
        public ServiceProvider(IServiceProvider parent)
            : this(new Func<IServiceProvider>[] { () => parent })
        {
        }

        /// <summary>
        /// Create a service provider with parent provider and service factories
        /// </summary>
        /// <param name="parents">an array of functions to return the next provider to search if service isn't found in this instance</param>
        public ServiceProvider(Func<IServiceProvider>[] parents) 
        {
            _parents = parents;
            _factories = new Dictionary<Type, Func<object>>();
            _services = new Dictionary<Type, object>();
        }

        /// <summary>
        /// Add service factory and cache result when requested.
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory<T>(Func<object> factory)
        {
            _factories.Add(typeof(T), () => {
                object service = factory();
                _services.Add(typeof(T), service);
                return service;
            });
        }

        /// <summary>
        /// Add service factory. Lets the service decide on how the cache the result.
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactoryWithNoCaching<T>(Func<object> factory) => _factories.Add(typeof(T), factory);

        /// <summary>
        /// Adds a service or context to inject into an command.
        /// </summary>
        /// <typeparam name="T">type of service</typeparam>
        /// <param name="instance">service instance</param>
        public void AddService<T>(T instance) => AddService(typeof(T), instance);

        /// <summary>
        /// Add a service instance.
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="service">instance</param>
        public void AddService(Type type, object service) => _services.Add(type, service);

        /// <summary>
        /// Flushes the cached service instance for the specified type. Does not remove the service factory registered for the type.
        /// </summary>
        /// <param name="type">service type</param>
        public void RemoveService(Type type) => _services.Remove(type);

        /// <summary>
        /// Flushes all the cached instances of the services. Does not remove any of the service factories registered.
        /// </summary>
        public void FlushServices() => _services.Clear();

        /// <summary>
        /// Returns the instance of the service or returns null if service doesn't exist
        /// </summary>
        /// <param name="type">service type</param>
        /// <returns>service instance or null</returns>
        public object GetService(Type type)
        {
            if (!_services.TryGetValue(type, out object service))
            {
                if (_factories.TryGetValue(type, out Func<object> factory))
                {
                    service = factory();
                }
            }
            if (service == null)
            {
                foreach (Func<IServiceProvider> parent in _parents)
                {
                    service = parent()?.GetService(type);
                    if (service != null)
                    {
                        break;
                    }
                }
            }
            return service;
        }
    }
}
