﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class CpuProfileConfiguration : MonitoringSourceConfiguration
    {
        public override IList<EventPipeProvider> GetProviders() =>
            new EventPipeProvider[]
            {
                new EventPipeProvider(SampleProfilerProviderName, System.Diagnostics.Tracing.EventLevel.Informational),
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", System.Diagnostics.Tracing.EventLevel.Informational, (long) Tracing.Parsers.ClrTraceEventParser.Keywords.Default)
            };
    }
}
