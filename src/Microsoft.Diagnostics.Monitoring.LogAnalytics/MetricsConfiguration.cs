using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal sealed class MetricsConfiguration
    {
        public string TenantId { get; set; }
        public string ClientId { get; }
        public string ClientSecret { get; }

        public string Region { get; }

        public string ResourceId { get; }

        private MetricsConfiguration(string tenantId, string clientId, string clientSecret)
        {
            TenantId = tenantId;
            ClientId = clientId;
            ClientSecret = clientSecret;
        }

        public static T WithCredentials<T>(Func<MetricsConfiguration, T> action)
        {
            return default(T);
        }
    }
}
