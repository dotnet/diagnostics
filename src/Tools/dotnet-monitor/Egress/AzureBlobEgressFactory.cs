// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Diagnostics.Tools.Monitor.Egress.AzureStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress
{
    /// <summary>
    /// Creates <see cref="ConfiguredEgressProvider"/> for Azure blob storage egress.
    /// </summary>
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

            // If account key was not provided but the name was provided,
            // lookup the account key property value from EgressOptions.Properties
            if (string.IsNullOrEmpty(options.AccountKey) &&
                !string.IsNullOrEmpty(options.AccountKeyName))
            {
                if (TryGetPropertyValue(providerName, egressProperties, options.AccountKeyName, out string key))
                {
                    options.AccountKey = key;
                }
            }

            // If shared access signature (SAS) was not provided but the name was provided,
            // lookup the SAS property value from EgressOptions.Properties
            if (string.IsNullOrEmpty(options.SharedAccessSignature) &&
                !string.IsNullOrEmpty(options.SharedAccessSignatureName))
            {
                if (TryGetPropertyValue(providerName, egressProperties, options.SharedAccessSignatureName, out string signature))
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

        private bool TryGetPropertyValue(string providerName, IDictionary<string, string> egressProperties, string propertyName, out string value)
        {
            if (!egressProperties.TryGetValue(propertyName, out value))
            {
                Logger.LogWarning("Provider '{0}': Unable to find '{1}' key in egress properties.", providerName, propertyName);
                return false;
            }
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
                var streamOptions = new AzureBlobEgressStreamOptions();
                streamOptions.ContentType = contentType;
                FillBlobMetadata(streamOptions.Metadata, source);

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
                var streamOptions = new AzureBlobEgressStreamOptions();
                streamOptions.ContentType = contentType;
                FillBlobMetadata(streamOptions.Metadata, source);

                string blobUri = await _provider.EgressAsync(action, fileName, streamOptions, token);

                return new EgressResult("uri", blobUri);
            }

            private static void FillBlobMetadata(IDictionary<string, string> metadata, IEndpointInfo source)
            {
                // Activity metadata
                Activity activity = Activity.Current;
                if (null != activity)
                {
                    metadata.Add(
                        ActivityMetadataNames.ParentId,
                        activity.GetParentId());
                    metadata.Add(
                        ActivityMetadataNames.SpanId,
                        activity.GetSpanId());
                    metadata.Add(
                        ActivityMetadataNames.TraceId,
                        activity.GetTraceId());
                }

                // Artifact metadata
                metadata.Add(
                    ArtifactMetadataNames.ArtifactSource.ProcessId,
                    source.ProcessId.ToString(CultureInfo.InvariantCulture));
                metadata.Add(
                    ArtifactMetadataNames.ArtifactSource.RuntimeInstanceCookie,
                    source.RuntimeInstanceCookie.ToString("N"));
            }
        }

        /// <summary>
        /// Egress provider options for Azure blob storage with additional options.
        /// </summary>
        private class ConfigurationOptions : AzureBlobEgressProviderOptions
        {
            /// <summary>
            /// The name of the account key used to look up the value from the <see cref="EgressOptions.Properties"/> map.
            /// </summary>
            public string AccountKeyName { get; set; }

            /// <summary>
            /// The name of the shared access signature (SAS) used to look up the value from the <see cref="EgressOptions.Properties"/> map.
            /// </summary>
            public string SharedAccessSignatureName { get; set; }
        }
    }
}
