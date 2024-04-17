﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class GcCollectConfiguration : MonitoringSourceConfiguration
    {
        public GcCollectConfiguration()
        {
            RundownKeyword = (long)Tracing.Parsers.ClrTraceEventParser.Keywords.GC;
        }

        public override IList<EventPipeProvider> GetProviders() =>
            new EventPipeProvider[]
            {
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", System.Diagnostics.Tracing.EventLevel.Informational, (long) Tracing.Parsers.ClrTraceEventParser.Keywords.GC),
                new EventPipeProvider("Microsoft-Windows-DotNETRuntimePrivate", System.Diagnostics.Tracing.EventLevel.Informational, (long) Tracing.Parsers.ClrTraceEventParser.Keywords.GC),
            };
    }
}
