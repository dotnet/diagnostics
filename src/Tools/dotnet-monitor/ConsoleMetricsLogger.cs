// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal sealed class ConsoleMetricsLogger : IMetricsLogger
    {
        public void LogMetrics(Metric metric)
        {
            string json = JsonSerializer.Serialize(metric, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
            Console.WriteLine(json);
        }
        public void Dispose()
        {
        }
    }
}
