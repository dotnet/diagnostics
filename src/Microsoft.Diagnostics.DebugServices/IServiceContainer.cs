// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// The method used to create a service instance
    /// </summary>
    /// <param name="provider">service provider instance that this factory is registered to.</param>
    /// <returns>service instance</returns>
    public delegate object ServiceFactory(IServiceProvider provider);

    /// <summary>
    /// This is returned by the service manager to allow various sub-components like 
    /// targets, modules, threads, runtimes to manage the services they provide. Calls
    /// the service factory for the type if not already instantiated and cached and if
    /// no factory, chains to the parent service container.
    /// </summary>
    public interface IServiceContainer
    {
        /// <summary>
        /// Returns the IServiceProvider instance
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Creates a copy container with the factories for the services and the parent
        /// in the container. Instantiated services are not transferred.
        /// </summary>
        /// <returns>clone</returns>
        IServiceContainer Clone();

        /// <summary>
        /// Add a service factory. Multiple factories for the same type are allowed.
        /// </summary>
        /// <param name="type">service type or interface</param>
        /// <param name="factory">function to create service instance</param>
        void AddServiceFactory(Type type, ServiceFactory factory);

        /// <summary>
        /// Add a service instance. Multiple instances for the same type are NOT allowed.
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="service">service instance (must derives from type)</param>
        void AddService(Type type, object service);

        /// <summary>
        /// Flushes the cached service instance for the specified type. Does not remove the service factory registered for the type.
        /// </summary>
        /// <param name="type">service type</param>
        void RemoveService(Type type);

        /// <summary>
        /// Flushes all the cached instances of the services. Does not remove any of the service factories registered.
        /// </summary>
        void FlushServices();

        /// <summary>
        /// Dispose of the instantiated services.
        /// </summary>
        /// <param name="thisService">skip this service to prevent recursion</param>
        void DisposeServices(object thisService);

        /// <summary>
        /// Get the cached/instantiated service instance if one exists. Don't call the factory or parent to create.
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="service">service instance (can be null)</param>
        /// <returns>if true, found service</returns>
        bool TryGetCachedService(Type type, out object service);
    }
}
