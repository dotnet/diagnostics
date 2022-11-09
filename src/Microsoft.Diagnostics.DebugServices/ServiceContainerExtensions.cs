// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ServiceContainerExtensions
    {
        /// <summary>
        /// Add a service factory. Multiple factories for the same type are allowed.
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <param name="container">IServiceContainer instance</param>
        /// <param name="factory">function to create service instance</param>
        public static void AddServiceFactory<T>(this IServiceContainer container, ServiceFactory factory) => container.AddServiceFactory(typeof(T), factory);

        /// <summary>
        /// Add a service instance. Multiple instances for the same type are not allowed.
        /// </summary>
        /// <typeparam name="T">type of service</typeparam>
        /// <param name="container">IServiceContainer instance</param>
        /// <param name="instance">service instance (must derive from T)</param>
        public static void AddService<T>(this IServiceContainer container, T instance) => container.AddService(typeof(T), instance);

        /// <summary>
        /// Get the cached/instantiated service instance if one exists. Don't call the factory or parent to create.
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <returns>service instance or null</returns>
        public static T GetCachedService<T>(this IServiceContainer container)
        {
            container.TryGetCachedService(typeof(T), out object service);
            return (T)service;
        }
    }
}
