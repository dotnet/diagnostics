// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    internal class AzureBlobEgressEndpointOptions :
        EgressEndpointOptions
    {
        public string ContainerName { get; set; }

        public string AccountUri { get; set; }

        public string AccountKey { get; set; }

        public string AccountKeyName { get; set; }

        public string SharedAccessSignature { get; set; }

        public string SharedAccessSignatureName { get; set; }

        public string BlobDirectory { get; set; }
    }
}
