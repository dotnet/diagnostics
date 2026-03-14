// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class CounterMetadata
    {
        public CounterMetadata(string providerName, string counterName, string counterUnit)
            : this(
                  providerName,
                  providerVersion: null,
                  counterName,
                  counterUnit,
                  counterDescription: null,
                  instrumentId: null,
                  meterTags: null,
                  instrumentTags: null,
                  scopeHash: null)
        {
        }

        internal CounterMetadata(string providerName, string counterName, string meterTags, string instrumentTags)
            : this(
                  providerName,
                  providerVersion: null,
                  counterName,
                  counterUnit: null,
                  counterDescription: null,
                  instrumentId: null,
                  meterTags,
                  instrumentTags,
                  scopeHash: null)
        {
        }

        public CounterMetadata(string providerName, string counterName, string meterTags, string instrumentTags, string scopeHash)
            : this(
                  providerName,
                  providerVersion: null,
                  counterName,
                  counterUnit: null,
                  counterDescription: null,
                  instrumentId: null,
                  meterTags,
                  instrumentTags,
                  scopeHash)
        {
        }

        public CounterMetadata(string providerName, string providerVersion, string counterName, string counterUnit, string counterDescription, int? instrumentId, string meterTags, string instrumentTags, string scopeHash)
        {
            ProviderName = providerName;
            ProviderVersion = providerVersion;
            CounterName = counterName;
            CounterUnit = counterUnit;
            CounterDescription = counterDescription;
            InstrumentId = instrumentId;
            MeterTags = meterTags;
            InstrumentTags = instrumentTags;
            ScopeHash = scopeHash;
        }

        public CounterMetadata() { }

        public string ProviderName { get; private set; }
        public string ProviderVersion { get; private set; }
        public string CounterName { get; private set; }
        public string CounterUnit { get; private set; }
        public string CounterDescription { get; private set; }
        public int? InstrumentId { get; private set; }
        public string MeterTags { get; private set; }
        public string InstrumentTags { get; private set; }
        public string ScopeHash { get; private set; }
    }
}
