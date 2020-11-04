// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.Egress.FileSystem;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class FileSystemEgressFactory : EgressFactory
    {
        private ILoggerFactory _loggerFactory;

        public FileSystemEgressFactory(ILoggerFactory loggerFactory)
            : base(loggerFactory.CreateLogger<FileSystemEgressFactory>())
        {
            _loggerFactory = loggerFactory;
        }

        public override bool TryCreate(
            string providerName,
            IConfigurationSection providerSection,
            Dictionary<string, string> egressProperties,
            out ConfiguredEgressProvider provider)
        {
            var options = providerSection.Get<FileSystemEgressProviderOptions>();

            if (!TryValidateOptions(options, providerName))
            {
                provider = null;
                return false;
            }

            provider = new Provider(options, _loggerFactory);
            return true;
        }

        private class Provider : ConfiguredEgressProvider
        {
            private readonly ILoggerFactory _loggerFactory;
            private readonly FileSystemEgressProviderOptions _options;

            public Provider(FileSystemEgressProviderOptions options, ILoggerFactory loggerFactory)
            {
                _loggerFactory = loggerFactory;
                _options = options;
            }

            public override async Task<EgressResult> EgressAsync(
                Func<CancellationToken, Task<Stream>> action,
                string fileName,
                string contentType,
                IEndpointInfo source,
                CancellationToken token)
            {
                var streamOptions = new FileSystemEgressStreamOptions();

                var provider = new FileSystemEgressProvider(_options, _loggerFactory.CreateLogger<FileSystemEgressProvider>());
                string filepath = await provider.EgressAsync(action, fileName, streamOptions, token);

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

                var provider = new FileSystemEgressProvider(_options, _loggerFactory.CreateLogger<FileSystemEgressProvider>());
                string filepath = await provider.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("path", filepath);
            }
        }
    }
}
