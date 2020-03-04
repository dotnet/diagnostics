// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    /// <summary>
    /// Do not rename these fields. These are used to bind to the app's configuration.
    /// </summary>
    public sealed class MetricsConfiguration
    {
        public string TenantId { get; set; }
        public string AadClientId { get; set; }
        public string AadClientSecret { get; set; }
    }

    public sealed class ResourceConfiguration
    {
        public string AzureResourceId { get; set; }
        public string AzureRegion { get; set; }
    }
}
