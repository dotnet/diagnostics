// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
            Timestamp = timestamp;
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
