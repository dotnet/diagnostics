// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
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

        internal void Log(ILogger logger)
        {
            logger.LogStreamOption(nameof(ContentEncoding), ContentEncoding);
            logger.LogStreamOption(nameof(ContentType), ContentType);
            logger.LogStreamOption(nameof(Metadata), "[" + string.Join(", ", Metadata.Keys) + "]");
        }
    }
}
