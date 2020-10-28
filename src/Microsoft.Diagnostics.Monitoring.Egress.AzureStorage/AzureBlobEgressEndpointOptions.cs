// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    internal class AzureBlobEgressEndpointOptions
    {
        public AzureBlobEgressEndpointOptions()
        {
        }

        public AzureBlobEgressEndpointOptions(AzureBlobEgressEndpointOptions settings)
        {
            ContainerName = settings.ContainerName;
            AccountName = settings.AccountName;
            SasToken = settings.SasToken;
            BlobDirectoryPath = settings.BlobDirectoryPath;
        }

        public string ContainerName { get; set; }

        public string AccountName { get; set; }

        public string SasToken { get; set; }

        public string BlobDirectoryPath { get; set; }
    }
}
