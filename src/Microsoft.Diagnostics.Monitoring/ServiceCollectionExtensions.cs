// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Diagnostics.Monitoring
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDiagnosticsConnectionSource(this IServiceCollection services, string reversedServerAddress, int? maxConnections = null)
        {
            if (string.IsNullOrWhiteSpace(reversedServerAddress))
            {
                return services.AddSingleton<IDiagnosticsConnectionsSource, ClientConnectionsSource>();
            }
            else
            {
                // Construct the source now rather than delayed construction
                // in order to be able to accept diagnostics connections immediately.
                var serverSource = new ReversedServerConnectionsSource(reversedServerAddress);
                serverSource.Listen(maxConnections.GetValueOrDefault(ReversedDiagnosticsServer.MaxAllowedConnections));

                return services.AddSingleton<IDiagnosticsConnectionsSource>(serverSource);
            }
        }
    }
}
