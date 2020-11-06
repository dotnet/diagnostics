// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.Egress.AzureStorage;
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
    internal class AzureBlobEgressFactory : EgressFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public AzureBlobEgressFactory(ILoggerFactory loggerFactory)
            : base(loggerFactory.CreateLogger<AzureBlobEgressFactory>())
        {
            _loggerFactory = loggerFactory;
        }

        public override bool TryCreate(
            string providerName,
            IConfigurationSection providerSection,
            Dictionary<string, string> egressProperties,
            out ConfiguredEgressProvider provider)
        {
            var options = providerSection.Get<ConfigurationOptions>();

            if (string.IsNullOrEmpty(options.AccountKey) &&
                !string.IsNullOrEmpty(options.AccountKeyName))
            {
                if (egressProperties.TryGetValue(options.AccountKeyName, out string key))
                {
                    options.AccountKey = key;
                }
            }

            if (string.IsNullOrEmpty(options.SharedAccessSignature) &&
                !string.IsNullOrEmpty(options.SharedAccessSignatureName))
            {
                if (egressProperties.TryGetValue(options.SharedAccessSignatureName, out string signature))
                {
                    options.SharedAccessSignature = signature;
                }
            }

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
            private readonly AzureBlobEgressProvider _provider;

            public Provider(AzureBlobEgressProviderOptions options, ILoggerFactory loggerFactory)
            {
                _provider = new AzureBlobEgressProvider(options, loggerFactory.CreateLogger<AzureBlobEgressProvider>());
            }

            public override async Task<EgressResult> EgressAsync(
                Func<CancellationToken, Task<Stream>> action,
                string fileName,
                string contentType,
                IEndpointInfo source,
                CancellationToken token)
            {
                // TODO: Add metadata based on source
                var streamOptions = new AzureBlobEgressStreamOptions();
                streamOptions.ContentType = contentType;

                string blobUri = await _provider.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("uri", blobUri);
            }

            public override async Task<EgressResult> EgressAsync(
                Func<Stream, CancellationToken, Task> action,
                string fileName,
                string contentType,
                IEndpointInfo source,
                CancellationToken token)
            {
                // TODO: Add metadata based on source
                var streamOptions = new AzureBlobEgressStreamOptions();
                streamOptions.ContentType = contentType;

                string blobUri = await _provider.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("uri", blobUri);
            }
        }

        private class ConfigurationOptions : AzureBlobEgressProviderOptions
        {
            public string AccountKeyName { get; set; }

            public string SharedAccessSignatureName { get; set; }
        }
    }
}
