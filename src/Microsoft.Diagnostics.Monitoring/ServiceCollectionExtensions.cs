// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Diagnostics.Monitoring
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddEndpointInfoSource(this IServiceCollection services, string reversedServerAddress, int? maxConnections = null)
        {
            // Create and add port description
            IDiagnosticPortDescription portDescription = DiagnosticPortDescription.Parse(reversedServerAddress);
            services.AddSingleton(portDescription);

            // Create and add endpoint info source
            switch (portDescription.Mode)
            {
                case DiagnosticPortConnectionMode.Connect:
                    services.AddSingleton<IEndpointInfoSource, ClientEndpointInfoSource>();
                    break;
                case DiagnosticPortConnectionMode.Listen:
                    // Construct the source now rather than delayed construction
                    // in order to be able to accept diagnostics connections immediately.
                    var serverSource = new ServerEndpointInfoSource(portDescription.Name);
                    serverSource.Listen(maxConnections.GetValueOrDefault(ReversedDiagnosticsServer.MaxAllowedConnections));
                    services.AddSingleton<IEndpointInfoSource>(serverSource);
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled connection mode: {portDescription.Mode}");
            }

            return services;
        }
    }
}
