// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ServiceManagerExtensions
    {
        /// <summary>
        /// Creates a new service container with all the registered factories for the given scope.
        /// </summary>
        /// <param name="serviceManager">service manager instance</param>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="parent">parent service provider to chain to</param>
        /// <returns>IServiceContainer instance</returns>
        public static ServiceContainer CreateServiceContainer(this IServiceManager serviceManager, ServiceScope scope, IServiceProvider parent)
        {
            ServiceContainerFactory containerFactory = serviceManager.CreateServiceContainerFactory(scope, parent);
            return containerFactory.Build();
        }
    }
}
