using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    public abstract class MonitoringSinkConfiguration
    {
        public abstract void AddMetricsLogger(IList<IMetricsLogger> metrics);

        public abstract void AddLogger(ILoggingBuilder builder);
    }
}
