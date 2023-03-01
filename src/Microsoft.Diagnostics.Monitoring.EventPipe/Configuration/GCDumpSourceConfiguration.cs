// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class GCDumpSourceConfiguration : MonitoringSourceConfiguration
    {
        public GCDumpSourceConfiguration()
        {
            RequestRundown = false;
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            var providers = new List<EventPipeProvider>()
            {
                new EventPipeProvider(
                    "Microsoft-Windows-DotNETRuntime",
                    EventLevel.Verbose,
                    (long)ClrTraceEventParser.Keywords.GCHeapSnapshot)
            };

            return providers;
        }
    }
}
