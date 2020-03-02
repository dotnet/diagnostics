using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal sealed class MetricsConfiguration
    {
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }

        public string Region { get; set; }

        public string ResourceId { get; set; }
    }
}
