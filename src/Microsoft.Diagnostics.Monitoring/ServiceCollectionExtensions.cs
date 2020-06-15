// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Diagnostics.Monitoring
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDiagnosticsConnectionSource(this IServiceCollection services, string transportPath)
        {
            if (string.IsNullOrWhiteSpace(transportPath))
            {
                return services.AddSingleton<IDiagnosticsConnectionsSource, ClientConnectionsSource>();
            }
            else
            {
                // Construct the source now rather than delayed construction
                // in order to be able to accept diagnostics connections immediately.
                return services.AddSingleton<IDiagnosticsConnectionsSource>(new ReversedServerConnectionsSource(transportPath));
            }
        }
    }
}
