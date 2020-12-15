﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
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
