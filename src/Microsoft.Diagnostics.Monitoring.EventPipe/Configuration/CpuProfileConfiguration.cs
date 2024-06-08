// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class CpuProfileConfiguration : MonitoringSourceConfiguration
    {
        public override IList<EventPipeProvider> GetProviders() =>
            new EventPipeProvider[]
            {
                new(SampleProfilerProviderName, System.Diagnostics.Tracing.EventLevel.Informational),
                new("Microsoft-Windows-DotNETRuntime", System.Diagnostics.Tracing.EventLevel.Informational, (long) Tracing.Parsers.ClrTraceEventParser.Keywords.Default)
            };
    }
}
