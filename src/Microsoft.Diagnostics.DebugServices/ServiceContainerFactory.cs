// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// The method used to create a service instance
    /// </summary>
    /// <param name="provider">service provider instance that this factory is registered to.</param>
    /// <returns>service instance</returns>
    public delegate object ServiceFactory(IServiceProvider provider);

    /// <summary>
    /// </summary>
    public class ServiceContainerFactory
    {
        private readonly Dictionary<Type, ServiceFactory> _factories;
        private readonly IServiceProvider _parent;
        private bool _finalized;

        /// <summary>
        /// Build a service container factory with parent provider and service factories
        /// </summary>
        /// <param name="parent">search this provider if service isn't found in this instance or null</param>
        /// <param name="factories">service factories to initialize provider or null</param>
        public ServiceContainerFactory(IServiceProvider parent, IDictionary<Type, ServiceFactory> factories)
        {
            Debug.Assert(factories != null);
            _parent = parent;
            _factories = new Dictionary<Type, ServiceFactory>(factories);
        }

        /// <summary>
        /// Add a service factory.
        /// </summary>
        /// <param name="type">service type or interface</param>
        /// <param name="factory">function to create service instance</param>
        /// <exception cref="ArgumentNullException">thrown if type or factory is null</exception>
        /// <exception cref="InvalidOperationException">thrown if factory has been finalized</exception>
        public void AddServiceFactory(Type type, ServiceFactory factory)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (_finalized)
            {
                throw new InvalidOperationException();
            }

            _factories.Add(type, factory);
        }

        /// <summary>
        /// Add a service factory.
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory<T>(ServiceFactory factory) => AddServiceFactory(typeof(T), factory);

        /// <summary>
        /// Removes the factory for the specified type.
        /// </summary>
        /// <param name="type">service type</param>
        /// <exception cref="ArgumentNullException">thrown if type is null</exception>
        /// <exception cref="InvalidOperationException">thrown if factory has been finalized</exception>
        public void RemoveServiceFactory(Type type)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (_finalized)
            {
                throw new InvalidOperationException();
            }

            _factories.Remove(type);
        }

        /// <summary>
        /// Remove a service factory.
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        public void RemoveServiceFactory<T>() => RemoveServiceFactory(typeof(T));

        /// <summary>
        /// Creates a new service container/provider instance and marks this factory as finalized. No more factories can be added or removed.
        /// </summary>
        /// <exception cref="InvalidOperationException">thrown if factory has not been finalized</exception>
        /// <returns>service container/provider instance</returns>
        public ServiceContainer Build()
        {
            _finalized = true;
            return new ServiceContainer(_parent, _factories);
        }

        /// <summary>
        /// Creates a copy of the container factory with the service factories and the parent service provider.
        /// </summary>
        /// <exception cref="InvalidOperationException">thrown if factory has not been finalized</exception>
        /// <returns>clone</returns>
        public ServiceContainerFactory Clone()
        {
            if (!_finalized)
            {
                throw new InvalidOperationException();
            }

            return new ServiceContainerFactory(_parent, _factories);
        }
    }
}
