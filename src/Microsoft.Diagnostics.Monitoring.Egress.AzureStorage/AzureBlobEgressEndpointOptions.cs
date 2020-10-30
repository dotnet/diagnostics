// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    internal class AzureBlobEgressEndpointOptions :
        EgressEndpointOptions
    {
        public AzureBlobEgressEndpointOptions()
        {
        }

        public AzureBlobEgressEndpointOptions(AzureBlobEgressEndpointOptions settings)
        {
            ContainerName = settings.ContainerName;
            AccountUri = settings.AccountUri;
            SharedAccessSignature = settings.SharedAccessSignature;
            BlobDirectory = settings.BlobDirectory;
        }

        public string ContainerName { get; set; }

        public string AccountUri { get; set; }

        public string SharedAccessSignature { get; set; }

        public string BlobDirectory { get; set; }
    }
}
