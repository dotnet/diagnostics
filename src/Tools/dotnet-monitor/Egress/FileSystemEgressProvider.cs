// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.Egress.FileSystem;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class FileSystemEgressProvider : EgressProvider
    {
        public override bool TryParse(string endpointName, IConfigurationSection config, out ConfiguredEgressEndpoint endpoint)
        {
            var optionsTemplate = config.Get<FileSystemEgressEndpointOptions>();

            // TODO: Validate options

            endpoint = new Endpoint(optionsTemplate);
            return true;
        }

        private class Endpoint : ConfiguredEgressEndpoint
        {
            private readonly FileSystemEgressEndpointOptions _optionsTemplate;

            public Endpoint(FileSystemEgressEndpointOptions optionsTemplate)
            {
                _optionsTemplate = optionsTemplate;
            }

            public override async Task<EgressResult> EgressAsync(
                Func<CancellationToken, Task<Stream>> action,
                string fileName,
                string contentType,
                IEndpointInfo source,
                CancellationToken token)
            {
                var streamOptions = new FileSystemEgressStreamOptions();

                var endpoint = new FileSystemEgressEndpoint(CreateEndpointOptions());
                string filepath = await endpoint.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("path", filepath);
            }

            public override async Task<EgressResult> EgressAsync(
                Func<Stream, CancellationToken, Task> action,
                string fileName,
                string contentType,
                IEndpointInfo source,
                CancellationToken token)
            {
                var streamOptions = new FileSystemEgressStreamOptions();

                var endpoint = new FileSystemEgressEndpoint(CreateEndpointOptions());
                string filepath = await endpoint.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("path", filepath);
            }

            private FileSystemEgressEndpointOptions CreateEndpointOptions()
            {
                return new FileSystemEgressEndpointOptions(_optionsTemplate);
            }
        }
    }
}
