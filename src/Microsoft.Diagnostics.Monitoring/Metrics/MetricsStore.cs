// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// Stores metrics, and produces a snapshot in Prometheus exposition format.
    /// </summary>
    public sealed class MetricsStore : IMetricsStore
    {
        private static readonly Dictionary<string, string> KnownUnits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {string.Empty, string.Empty},
            {"count", string.Empty},
            {"B", "_bytes" },
            {"MB", "_bytes" },
            {"%", "_ratio" },
        };

        private sealed class MetricKey
        {
            private Metric _metric;

            public MetricKey(Metric metric)
            {
                _metric = metric;
            }

            public override int GetHashCode()
            {
                HashCode code = new HashCode();
                code.Add(_metric.Name);
                foreach(string name in _metric.DimNames)
                {
                    code.Add(name);
                }
                foreach(string value in _metric.DimValues)
                {
                    code.Add(value);
                }
                return code.ToHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is MetricKey metricKey)
                {
                    return CompareMetrics(_metric, metricKey._metric);
                }
                return false;
            }
        }

        private Dictionary<MetricKey, Queue<Metric>> _allMetrics = new Dictionary<MetricKey, Queue<Metric>>();
        private readonly int _maxMetricCount;

        public MetricsStore(int maxMetricCount)
        {
            if (maxMetricCount < 1)
            {
                throw new ArgumentException("Invalid metric count");
            }
            _maxMetricCount = maxMetricCount;
        }

        public void AddMetric(Metric metric)
        {
            lock (_allMetrics)
            {
                var metricKey = new MetricKey(metric);
                if (!_allMetrics.TryGetValue(metricKey, out Queue<Metric> metrics))
                {
                    metrics = new Queue<Metric>();
                    _allMetrics.Add(metricKey, metrics);
                }
                metrics.Enqueue(metric);
                if (metrics.Count > _maxMetricCount)
                {
                    metrics.Dequeue();
                }
            }
        }

        public async Task SnapshotMetrics(Stream outputStream, CancellationToken token)
        {
            Dictionary<MetricKey, Queue<Metric>> copy = null;
            lock (_allMetrics)
            {
                copy = new Dictionary<MetricKey, Queue<Metric>>();
                foreach (var metricGroup in _allMetrics)
                {
                    copy.Add(metricGroup.Key, new Queue<Metric>(metricGroup.Value));
                }
            }

            using var writer = new StreamWriter(outputStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true);
            writer.NewLine = "\n";

            foreach (var metricGroup in copy)
            {
                Metric metricInfo = metricGroup.Value.First();
                string metricName = GetPrometheusMetric(metricInfo, out string metricValue);
                string metricType = "gauge";

                //TODO Some clr metrics claim to be incrementing, but are really gauges.

                await writer.WriteLineAsync(FormattableString.Invariant($"# HELP {metricName} {metricInfo.DisplayName}"));
                await writer.WriteLineAsync(FormattableString.Invariant($"# TYPE {metricName} {metricType}"));

                foreach (var metric in metricGroup.Value)
                {
                    await WriteMetricDetails(writer, metric, metricName, metricValue);
                }
            }
        }

        private static async Task WriteMetricDetails(
                    StreamWriter writer,
                    Metric metric,
                    string metricName,
                    string metricValue)
        {
            await writer.WriteAsync(metricName);
            if (metric.DimNames.Count > 0)
            {
                await writer.WriteAsync('{');
                await WriteNameValuePairs(writer, metric);
                await writer.WriteAsync('}');
            }
            await writer.WriteLineAsync(FormattableString.Invariant($" {metricValue} {new DateTimeOffset(metric.Timestamp).ToUnixTimeMilliseconds()}"));
        }

        private static async Task WriteNameValuePairs(StreamWriter writer, Metric metric)
        {
            for (int i = 0; i < metric.DimNames.Count; i++)
            {
                if (i > 0)
                {
                    writer.Write(',');
                }
                await writer.WriteAsync(FormattableString.Invariant($"{metric.DimNames[i]}=\"{metric.DimValues[i]}\""));
            }
        }

        private static string GetPrometheusMetric(Metric metric, out string metricValue)
        {
            string unitSuffix = string.Empty;

            if ((metric.Unit != null) && (!KnownUnits.TryGetValue(metric.Unit, out unitSuffix)))
            {
                //TODO The prometheus data model does not allow certain characters. Units we are not expecting could cause a scrape failure.
                unitSuffix = "_" + metric.Unit;
            }

            double value = metric.Value;
            if (string.Equals(metric.Unit, "MB", StringComparison.OrdinalIgnoreCase))
            {
                value *= 1_000_000; //Note that the metric uses MB not MiB
            }

            metricValue = value.ToString(CultureInfo.InvariantCulture);
            return FormattableString.Invariant($"{metric.Namespace.Replace(".", string.Empty).ToLowerInvariant()}_{metric.Name.Replace('-', '_')}{unitSuffix}");
        }

        private static bool CompareMetrics(Metric first, Metric second)
        {
            if (!string.Equals(first.Name, second.Name))
            {
                return false;
            }
            if (first.DimNames.Count != second.DimNames.Count)
            {
                return false;
            }
            for (int i = 0; i < first.DimNames.Count; i++)
            {
                if (first.DimNames[i] != second.DimNames[i])
                {
                    return false;
                }
                if (first.DimValues[i] != second.DimValues[i])
                {
                    return false;
                }
            }
            return true;
        }

        public void Clear()
        {
            lock (_allMetrics)
            {
                _allMetrics.Clear();
            }
        }

        public void Dispose()
        {
        }
    }
}
