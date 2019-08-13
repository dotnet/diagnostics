// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    public class ServiceProvider : IServiceProvider
    {
        readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        readonly Dictionary<Type, Func<object>> _factories = new Dictionary<Type, Func<object>>();

        /// <summary>
        /// Create a service provider instance
        /// </summary>
        public ServiceProvider()
        {
        }

        /// <summary>
        /// Add service factory
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="factory">function to create service instance</param>
        public void AddServiceFactory(Type type, Func<object> factory) => _factories.Add(type, factory);

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
                    _services.Add(type, service);
                }
            }
            return service;
        }
    }

    public static class ServiceProviderExtensions
    {
        /// <summary>
        /// Returns the instance of the service or returns null if service doesn't exist
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <returns>service instance or null</returns>
        public static T GetService<T>(this IServiceProvider serviceProvider)
        {
            return (T)serviceProvider.GetService(typeof(T));
        }
    }
}
