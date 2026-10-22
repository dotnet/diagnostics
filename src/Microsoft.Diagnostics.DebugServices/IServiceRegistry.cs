// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Registers lazy service and provider factories before the host creates its service containers.
    /// </summary>
    public interface IServiceRegistry
    {
        void AddServiceFactory<TService>(ServiceScope scope, Func<IServiceProvider, TService> factory) where TService : class;

        void AddProviderFactory<TProvider>(Func<IServiceProvider, TProvider> factory) where TProvider : class;
    }
}
