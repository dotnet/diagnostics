// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class ActivitySourceConfiguration : MonitoringSourceConfiguration
    {
        private readonly double _samplingRatio;
        private readonly string[] _activitySourceNames;

        public ActivitySourceConfiguration(
            double samplingRatio,
            IEnumerable<string>? activitySourceNames)
        {
            _samplingRatio = samplingRatio;
            _activitySourceNames = activitySourceNames?.ToArray() ?? Array.Empty<string>();
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            StringBuilder filterAndPayloadSpecs = new();
            foreach (string activitySource in _activitySourceNames)
            {
                if (string.IsNullOrEmpty(activitySource))
                {
                    continue;
                }

                // Note: It isn't currently possible to get Events or Links off
                // of Activity using this mechanism:
                // Events=Events.*Enumerate;Links=Links.*Enumerate; See:
                // https://github.com/dotnet/runtime/issues/102924

                string sampler = string.Empty;

                if (_samplingRatio < 1D)
                {
                    sampler = $"-ParentRatioSampler({_samplingRatio})";
                }

                filterAndPayloadSpecs.AppendLine($"[AS]{activitySource}/Stop{sampler}:-TraceId;SpanId;ParentSpanId;ActivityTraceFlags;TraceStateString;Kind;DisplayName;StartTimeTicks=StartTimeUtc.Ticks;DurationTicks=Duration.Ticks;Status;StatusDescription;Tags=TagObjects.*Enumerate;ActivitySourceVersion=Source.Version");
            }

            // Note: Microsoft-Diagnostics-DiagnosticSource only supports a
            // single listener. There can only be one
            // ActivitySourceConfiguration, AspNetTriggerSourceConfiguration, or
            // HttpRequestSourceConfiguration in play.
            return new[] {
                new EventPipeProvider(
                    DiagnosticSourceEventSource,
                    keywords: DiagnosticSourceEventSourceEvents | DiagnosticSourceEventSourceMessages,
                    eventLevel: EventLevel.Verbose,
                    arguments: new Dictionary<string, string>()
                    {
                        { "FilterAndPayloadSpecs", filterAndPayloadSpecs.ToString() },
                    })
            };
        }
    }
}
