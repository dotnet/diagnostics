// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Diagnostics.DebugServices
{
    public static class ServiceRegistryExtensions
    {
        public static void AddService<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceRegistry registry, ServiceScope scope)
            where TService : class
            where TImplementation : class, TService
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            registry.AddServiceFactory<TService>(scope, CreateServiceFactory<TImplementation>());
        }

        public static void AddService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
            this IServiceRegistry registry, ServiceScope scope) where T : class =>
            registry.AddService<T, T>(scope);

        public static void AddProvider<TProvider, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceRegistry registry)
            where TProvider : class
            where TImplementation : class, TProvider
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            registry.AddProviderFactory<TProvider>(CreateServiceFactory<TImplementation>());
        }

        private static Func<IServiceProvider, TImplementation> CreateServiceFactory<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>() where TImplementation : class
        {
            ObjectFactory factory = ActivatorUtilities.CreateFactory(typeof(TImplementation), Type.EmptyTypes);
            return services => (TImplementation)factory(services, Array.Empty<object>());
        }
    }
}
