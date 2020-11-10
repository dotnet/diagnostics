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
    /// <summary>
    /// Creates <see cref="ConfiguredEgressProvider"/> for file system egress.
    /// </summary>
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
            private readonly FileSystemEgressProvider _provider;

            public Provider(FileSystemEgressProviderOptions options, ILoggerFactory loggerFactory)
            {
                _provider = new FileSystemEgressProvider(options, loggerFactory.CreateLogger<FileSystemEgressProvider>());
            }

            public override async Task<EgressResult> EgressAsync(
                Func<CancellationToken, Task<Stream>> action,
                string fileName,
                string contentType,
                IEndpointInfo source,
                CancellationToken token)
            {
                var streamOptions = new FileSystemEgressStreamOptions();

                string filepath = await _provider.EgressAsync(action, fileName, streamOptions, token);

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

                string filepath = await _provider.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("path", filepath);
            }
        }
    }
}
