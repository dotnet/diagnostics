// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal class MetricsRestClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly ResourceConfiguration _resourceConfig;

        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public MetricsRestClient(MetricsConfiguration config, ResourceConfiguration resourceConfig)
        {
            _resourceConfig = resourceConfig;
            _client = new HttpClient(new AuthenticationDelegatingHandler(config));
        }

        public async Task SendMetric(Metric metric, CancellationToken token)
        {
            string uri = FormattableString.Invariant($"https://{_resourceConfig.AzureRegion}.monitoring.azure.com{_resourceConfig.AzureResourceId}/metrics");

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri);

            var aggregatedMetric = new AggregatedMetric();

            aggregatedMetric.Data.BaseData.Namespace = metric.Namespace;
            aggregatedMetric.Data.BaseData.Metric = metric.DisplayName + (string.IsNullOrEmpty(metric.Unit) ? string.Empty : $" ({metric.Unit})");

            aggregatedMetric.Time = metric.Timestamp.ToString("o");
            aggregatedMetric.Data.BaseData.DimNames = metric.DimNames;

            var series = new MetricSeries
            {
                Count = 1,
                Sum = metric.Value,
                Min = metric.Value,
                Max = metric.Value,
                DimValues = metric.DimValues
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
            _client?.Dispose();
        }
    }
}
