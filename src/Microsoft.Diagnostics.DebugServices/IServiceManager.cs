// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    public interface IServiceManager
    {
        /// <summary>
        /// Creates a new service container factory with all the registered factories for the given scope.
        /// </summary>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="parent">parent service provider to chain to</param>
        /// <returns>IServiceContainerFactory instance</returns>
        ServiceContainerFactory CreateServiceContainerFactory(ServiceScope scope, IServiceProvider parent);

        /// <summary>
        /// Get the provider factories for a type or interface.
        /// </summary>
        /// <param name="providerType">type or interface</param>
        /// <returns>the provider factories for the type</returns>
        IEnumerable<ServiceFactory> EnumerateProviderFactories(Type providerType);
    }
}
