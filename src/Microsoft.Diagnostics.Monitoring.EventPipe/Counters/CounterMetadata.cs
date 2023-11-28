// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class CounterMetadata
    {
        public CounterMetadata(string providerName, string counterName, string meterTags, string instrumentTags, string scopeHash)
        {
            ProviderName = providerName;
            CounterName = counterName;
            MeterTags = meterTags;
            InstrumentTags = instrumentTags;
            ScopeHash = scopeHash;
        }

        public CounterMetadata() { }
        public string ProviderName { get; private set; }
        public string CounterName { get; private set; }
        public string MeterTags { get; private set; }
        public string InstrumentTags { get; private set; }
        public string ScopeHash { get; private set; }
    }
}
