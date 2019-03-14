// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CollectionConfiguration
    {
        public int? ProcessId { get; set; }
        public string OutputPath { get; set; }
        public int? CircularMB { get; set; }
        public int? Interval { get; set; }
        public IList<CounterProvider> Providers { get; set; } = new List<CounterProvider>();

        internal string ToConfigString()
        {
            var builder = new StringBuilder();
            if (ProcessId != null)
            {
                builder.AppendLine($"ProcessId={ProcessId.Value}");
            }
            if (!string.IsNullOrEmpty(OutputPath))
            {
                builder.AppendLine($"OutputPath={OutputPath}");
            }
            if (CircularMB != null)
            {
                builder.AppendLine($"CircularMB={CircularMB}");
            }
            if (Providers != null && Providers.Count > 0)
            {
                builder.AppendLine($"Providers={SerializeProviders(Providers)}");
            }
            return builder.ToString();
        }

        public void AddProvider(CounterProvider provider)
        {
            Providers.Add(provider);
        }

        private string SerializeProviders(IEnumerable<CounterProvider> providers) => string.Join(",", providers.Select(p => SerializeProvider(p)));

        private string SerializeProvider(CounterProvider provider)
        {
            return $"{provider.Name}:{provider.Keywords}:{provider.Level}:EventCounterIntervalSec={Interval}";
        }

    }
}
