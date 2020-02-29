using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal class MetricsRestClient : IDisposable
    {
        private HttpClient _client;
        private readonly string _region;
        private readonly string _resourceId;

        //We expect metric units, dimensions, and so on to stay the same throughout the session.
        Dictionary<(string metricNamespace, string metricName), AggregatedMetric> _metricCache = new Dictionary<(string metricNamespace, string metricName), AggregatedMetric>();

        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public MetricsRestClient(string region, string resourceId)
        {
            _client = new HttpClient(new AuthenticationDelegatingHandler());
            _region = region;
            _resourceId = resourceId;
        }

        public async Task SendMetric(Metric metric, CancellationToken token)
        {
            string uri = FormattableString.Invariant($"https://{_region}.monitoring.azure.com{_resourceId}/metrics");

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);

            if (!_metricCache.TryGetValue((metric.Namespace, metric.Name), out AggregatedMetric aggregatedMetric))
            {
                aggregatedMetric = new AggregatedMetric();
                _metricCache.Add((metric.Namespace, metric.Name), aggregatedMetric);

                aggregatedMetric.Data.BaseData.Namespace = metric.Namespace;
                aggregatedMetric.Data.BaseData.Metric = metric.DisplayName + (string.IsNullOrEmpty(metric.Unit) ? string.Empty : $" ({metric.Unit}");
            }

            aggregatedMetric.Time = metric.Timestamp.ToString("o");
            aggregatedMetric.Data.BaseData.DimNames = metric.DimNames;

            var series = new MetricSeries
            {
                Count = 1,
                Sum = metric.Value,
                Min = metric.Value,
                Max = metric.Value,
            };

            aggregatedMetric.Data.BaseData.Series.Add(series);


            using var memoryStream = new MemoryStream();
            await JsonSerializer.SerializeAsync(memoryStream, aggregatedMetric, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, token);
            memoryStream.Position = 0L;

            StreamContent streamContent = new StreamContent(memoryStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content = streamContent;

            await _client.SendAsync(request, token);
        }

        public void Dispose()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
        }
    }
}
