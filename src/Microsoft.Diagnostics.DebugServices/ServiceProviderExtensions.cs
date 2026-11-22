// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ServiceProviderExtensions
    {
        /// <summary>
        /// Returns the instance of the service or returns null if service doesn't exist
        /// </summary>
        /// <typeparam name="T">service type</typeparam>
        /// <returns>service instance or null</returns>
        public static T GetService<T>(this IServiceProvider serviceProvider) => (T)serviceProvider.GetService(typeof(T));

        /// <summary>
        /// Returns a required service or throws when the service is unavailable.
        /// </summary>
        public static T GetRequiredService<T>(this IServiceProvider serviceProvider)
        {
            if (serviceProvider is null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }
            object service = serviceProvider.GetService(typeof(T));
            if (service is null)
            {
                throw new DiagnosticsException($"The {typeof(T).Name} service is required");
            }
            return (T)service;
        }
    }
}
