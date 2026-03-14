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
    }
}
