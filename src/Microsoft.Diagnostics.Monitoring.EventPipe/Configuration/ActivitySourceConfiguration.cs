// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class ActivitySourceConfiguration : MonitoringSourceConfiguration
    {
        private readonly double _SamplingRatio;
        private readonly string[] _ActivitySourceNames;

        public ActivitySourceConfiguration(
            DiagnosticsClient client,
            double samplingRatio,
            IEnumerable<string>? activitySourceNames)
        {
            _SamplingRatio = samplingRatio;
            _ActivitySourceNames = activitySourceNames?.ToArray() ?? Array.Empty<string>();

            if (_SamplingRatio < 1D)
            {
                int majorVersion = 0;

                using CancellationTokenSource cancellationTokenSource = new();

                DiagnosticsEventPipeProcessor processor = new(
                    new ActivitySourceConfiguration(client, 1D, activitySourceNames: null),
                    async (EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token) => {
                        eventSource.Dynamic.All += traceEvent => {
                            try
                            {
                                if ("Version".Equals(traceEvent.EventName))
                                {
                                    majorVersion = (int)traceEvent.PayloadValue(0);
                                }

                                if (!cancellationTokenSource.IsCancellationRequested)
                                {
                                    // Note: Version should be the first message
                                    // written so cancel once we have received a
                                    // message.
                                    cancellationTokenSource.Cancel();
                                }
                            }
                            catch (Exception)
                            {
                            }
                        };

                        using EventTaskSource<Action> sourceCompletedTaskSource = new(
                            taskComplete => taskComplete,
                            handler => eventSource.Completed += handler,
                            handler => eventSource.Completed -= handler,
                            token);

                        await sourceCompletedTaskSource.Task.ConfigureAwait(false);
                    });

                try
                {
                    processor.Process(client, TimeSpan.FromSeconds(10), resumeRuntime: false, token: cancellationTokenSource.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }

                processor.DisposeAsync().AsTask().GetAwaiter().GetResult();

                if (majorVersion < 9)
                {
                    throw new NotSupportedException("Sampling ratio can only be set when listening to processes running System.Diagnostics.DiagnosticSource 9 or greater");
                }
            }
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            StringBuilder filterAndPayloadSpecs = new();
            foreach (string activitySource in _ActivitySourceNames)
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

                if (_SamplingRatio < 1D)
                {
                    sampler = $"-ParentRatioSampler({_SamplingRatio})";
                }

                filterAndPayloadSpecs.AppendLine($"[AS]{activitySource}/Stop{sampler}:-TraceId;SpanId;ParentSpanId;ActivityTraceFlags;TraceStateString;Kind;DisplayName;StartTimeTicks=StartTimeUtc.Ticks;DurationTicks=Duration.Ticks;Status;StatusDescription;Tags=TagObjects.*Enumerate;ActivitySourceVersion=Source.Version");
            }

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
