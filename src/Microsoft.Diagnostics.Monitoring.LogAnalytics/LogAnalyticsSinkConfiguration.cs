using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    public sealed class LogAnalyticsSinkConfiguration : MonitoringSinkConfiguration
    {
        public override void AddLogger(ILoggingBuilder builder)
        {
            throw new NotImplementedException();
        }

        public override void AddMetricsLogger(IList<IMetricsLogger> metrics)
        {
            throw new NotImplementedException();
        }
    }
}
