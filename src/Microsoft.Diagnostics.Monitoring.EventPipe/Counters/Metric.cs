// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public enum MetricType
    {
        Avg,
        Sum,
        Min,
        Max
    }

    public class Metric : ICounterPayload
    {
        public Metric(DateTime timestamp,
            string metricNamespace,
            string name,
            string displayName,
            string unit,
            double value,
            MetricType metricType,
            float interval)
        {
            Timestamp = timestamp;
            Name = name;
            DisplayName = displayName;
            Unit = unit;
            Value = value;
            MetricType = metricType;
            Namespace = metricNamespace;
            Interval = interval;
        }

        private string Namespace { get; }

        private MetricType MetricType { get; }

        private string Name { get; }

        private string DisplayName { get; }

        private string Unit { get; }

        private double Value { get; }

        private DateTime Timestamp { get; }

        private float Interval { get; }

        public string GetCounterType()
        {
            return (MetricType == MetricType.Sum) ? "Rate" : "Metric";
        }

        public string GetName() => Name;

        public double GetValue() => Value;

        public string GetProvider() => Namespace;

        public string GetDisplayName() => DisplayName;

        public string GetUnit() => Unit;

        public DateTime GetTimestamp() => Timestamp;

        public float GetInterval() => Interval;
    }
}