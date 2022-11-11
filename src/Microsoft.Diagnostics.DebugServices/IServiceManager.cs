// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    public interface IServiceManager
    {
        /// <summary>
        /// Creates a new service provider instance with all the registered factories for the given scope.
        /// </summary>
        /// <param name="scope">global, per-target, per-runtime, etc. service type</param>
        /// <param name="parent">parent service provider to chain to</param>
        /// <returns>IServiceContainer instance</returns>
        IServiceContainer CreateServiceContainer(ServiceScope scope, IServiceProvider parent);
    }
}
