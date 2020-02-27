using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    public interface IMetricsLogger : IDisposable
    {
        void LogMetrics(Metric metric);
    }
}
