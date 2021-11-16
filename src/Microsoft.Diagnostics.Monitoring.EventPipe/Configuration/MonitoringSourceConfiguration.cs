// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public abstract class MonitoringSourceConfiguration
    {
        /// <summary>
        /// Indicates diagnostics messages from DiagnosticSourceEventSource should be included.
        /// </summary>
        public const long DiagnosticSourceEventSourceMessages = 0x1;

        /// <summary>
        /// Indicates that all events from all diagnostic sources should be forwarded to the EventSource using the 'Event' event.
        /// </summary>
        public const long DiagnosticSourceEventSourceEvents = 0x2;

        public const string MicrosoftExtensionsLoggingProviderName = "Microsoft-Extensions-Logging";
        public const string SystemRuntimeEventSourceName = "System.Runtime";
        public const string MicrosoftAspNetCoreHostingEventSourceName = "Microsoft.AspNetCore.Hosting";
        public const string GrpcAspNetCoreServer = "Grpc.AspNetCore.Server";
        public const string DiagnosticSourceEventSource = "Microsoft-Diagnostics-DiagnosticSource";
        public const string TplEventSource = "System.Threading.Tasks.TplEventSource";
        public const string SampleProfilerProviderName = "Microsoft-DotNETCore-SampleProfiler";
        public const string EventPipeProviderName = "Microsoft-DotNETCore-EventPipe";

        public static IEnumerable<string> DefaultMetricProviders => new[] { SystemRuntimeEventSourceName, MicrosoftAspNetCoreHostingEventSourceName, GrpcAspNetCoreServer };

        public abstract IList<EventPipeProvider> GetProviders();

        public virtual bool RequestRundown { get; set; } = true;

        public virtual int BufferSizeInMB => 256;
    }
}
