// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        public const string SystemDiagnosticsMetricsProviderName = "System.Diagnostics.Metrics";

        private bool _requestRundown = true;
        private long _rundownKeyword = EventPipeSession.DefaultRundownKeyword;

        public static IEnumerable<string> DefaultMetricProviders => new[] { SystemRuntimeEventSourceName, MicrosoftAspNetCoreHostingEventSourceName, GrpcAspNetCoreServer };

        public abstract IList<EventPipeProvider> GetProviders();

        public virtual bool RequestRundown
        {
            get => _requestRundown;
            set
            {
                _requestRundown = value;
                if (_requestRundown)
                {
                    if (_rundownKeyword == 0)
                    {
                        _rundownKeyword = EventPipeSession.DefaultRundownKeyword;
                    }
                }
                else
                {
                    _rundownKeyword = 0;
                    RetryStrategy = RetryStrategy.DoNotRetry;
                }
            }
        }

        public virtual long RundownKeyword
        {
            get => _rundownKeyword;
            set
            {
                _rundownKeyword = value;
                if (_rundownKeyword == 0)
                {
                    _requestRundown = false;
                    RetryStrategy = RetryStrategy.DoNotRetry;
                }
                else
                {
                    RetryStrategy = RetryStrategy.DropKeywordKeepRundown;
                    _requestRundown = true;
                }
            }
        }

        public virtual int BufferSizeInMB => 256;

        public virtual RetryStrategy RetryStrategy { get; set; } = RetryStrategy.DropKeywordKeepRundown;
    }
}
