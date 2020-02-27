using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Microsoft.Diagnostics.Monitoring
{
    public enum MetricType
    {
        Avg,
        Sum,
        Min,
        Max
    }

    public class Metric
    {
        public Metric(DateTime timestamp, string metricNamespace, string name, string displayName, string unit, double value, IReadOnlyList<string> dimNames, IReadOnlyList<string> dimValues, MetricType metricType = MetricType.Avg)
        {
            Name = name;
            DisplayName = displayName;
            Unit = unit;
            Value = value;
            MetricType = metricType;
            Namespace = metricNamespace;
            DimNames = dimNames;
            DimValues = dimValues;
        }

        public IReadOnlyList<string> DimNames { get; }

        public IReadOnlyList<string> DimValues { get; }

        public string Namespace { get; }

        public MetricType MetricType { get; }

        public string Name { get; }

        public string DisplayName { get; }

        public string Unit { get; }

        public double Value { get; }

        public DateTime Timestamp { get; }
    }
}
