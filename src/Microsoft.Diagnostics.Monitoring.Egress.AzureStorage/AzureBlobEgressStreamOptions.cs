// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    /// <summary>
    /// Egress stream options for Azure blob storage.
    /// </summary>
    internal class AzureBlobEgressStreamOptions
    {
        /// <summary>
        /// The content encoding of the blob to be created.
        /// </summary>
        public string ContentEncoding { get; set; }

        /// <summary>
        /// The content type of the blob to be created.
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// The metadata of the blob to be created.
        /// </summary>
        public Dictionary<string, string> Metadata { get; }
            = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
