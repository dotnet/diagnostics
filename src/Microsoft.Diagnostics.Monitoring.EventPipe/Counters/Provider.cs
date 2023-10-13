// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class Provider
    {
        public Provider(string providerName, string meterTags, string instrumentTags, string scopeHash)
        {
            ProviderName = providerName;
            MeterTags = meterTags;
            InstrumentTags = instrumentTags;
            ScopeHash = scopeHash;
        }

        public Provider() { }
        public string ProviderName { get; private set; }
        public string MeterTags { get; private set; }
        public string InstrumentTags { get; private set; }
        public string ScopeHash { get; private set; }
    }
}
