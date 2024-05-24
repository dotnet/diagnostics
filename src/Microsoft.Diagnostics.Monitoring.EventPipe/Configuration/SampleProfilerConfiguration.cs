// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class SampleProfilerConfiguration : MonitoringSourceConfiguration
    {
        public SampleProfilerConfiguration()
        {
            RundownKeyword = 0;
        }

        public override IList<EventPipeProvider> GetProviders() =>
            new EventPipeProvider[]
            {
                new(SampleProfilerProviderName, EventLevel.Informational)
            };

        public override int BufferSizeInMB => 1;
    }
}
