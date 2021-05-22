// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// The method used to create a service instance
    /// </summary>
    /// <param name="provider">service provider instance that this factory belongs</param>
    /// <returns>service instance</returns>
    public delegate object ServiceFactory(IServiceProvider provider);

    /// <summary>
    /// This is returned by the service manager to allow various sub-components like 
    /// targets, modules, threads, runtimes to  managed the services they provider.
    ///
    /// Implementations of this interface need to allow multiple instances of the same 
    /// service type to be registered. They are queried by getting the IEnumerable of 
    /// the service type. If the non-enumerable service type is queried and there are
    /// multiple instances, an exception is thrown. The IRuntimeService implementation
    /// uses this feature to enumerate all the IRuntimeProvider instances registered 
    /// in the system.
    /// </summary>
    public interface IServiceContainer
    {
        /// <summary>
        /// Returns the IServiceProvider instance
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Clones the parent service provider except any cached instances.
        /// </summary>
        /// <returns>clone</returns>
        IServiceContainer Clone();

        /// <summary>
        /// Add service factory and cache result when requested.
        /// </summary>
        /// <param name="type">service type or interface</param>
        /// <param name="factory">function to create service instance</param>
        void AddServiceFactory(Type type, ServiceFactory factory);

        /// <summary>
        /// Add a service instance.
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="service">instance</param>
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
        /// Get the cached service instance if one exists. Don't call the factory or parent to create.
        /// </summary>
        /// <param name="type">service type</param>
        /// <param name="service">service instance (can be null)</param>
        /// <returns>if true, found service</returns>
        bool TryGetCachedService(Type type, out object service);
    }
}
