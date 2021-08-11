using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe.UnitTests
{
    internal static class EventCounterConstants
    {
        public const string RuntimeProviderName = "System.Runtime";

        public const string EventCountersEventName = "EventCounters";

        public const string CpuUsageCounterName = "cpu-usage";
        public const string CpuUsageDisplayName = "CPU Usage";
        public const string CpuUsageUnits = "%";
    }
}
