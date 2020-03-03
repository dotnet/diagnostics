using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    /// <summary>
    /// Do not rename these fields!
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
