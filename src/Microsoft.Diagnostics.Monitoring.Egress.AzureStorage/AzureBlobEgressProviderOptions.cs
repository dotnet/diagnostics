// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    internal class AzureBlobEgressProviderOptions :
        EgressProviderOptions
    {
        public string ContainerName { get; set; }

        public Uri AccountUri { get; set; }

        public string AccountKey { get; set; }

        public string SharedAccessSignature { get; set; }

        public string BlobPrefix { get; set; }
    }
}
