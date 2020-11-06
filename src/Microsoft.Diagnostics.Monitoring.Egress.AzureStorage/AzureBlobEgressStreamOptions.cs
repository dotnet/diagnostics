// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.Egress.AzureStorage
{
    internal class AzureBlobEgressStreamOptions
    {
        public string ContentEncoding { get; set; }

        public string ContentType { get; set; }

        public Dictionary<string, string> Metadata { get; }
            = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
