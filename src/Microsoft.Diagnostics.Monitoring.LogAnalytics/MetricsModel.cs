using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal sealed class MetricSeries
    {
        public IReadOnlyList<string> DimValues { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Sum { get; set; }
        public int Count { get; set; }
    }

    internal sealed class MetricBaseData
    {
        public string Metric { get; set; }
        public string Namespace { get; set; }
        public IReadOnlyList<string> DimNames { get; set; }
        public IList<MetricSeries> Series { get; set; } = new List<MetricSeries>();
    }

    internal sealed class MetricData
    {
        public MetricBaseData BaseData { get; set; } = new MetricBaseData();
    }

    internal sealed class AggregatedMetric
    {
        public string Time { get; set; }
        public MetricData Data { get; set; } = new MetricData();
    }
}
