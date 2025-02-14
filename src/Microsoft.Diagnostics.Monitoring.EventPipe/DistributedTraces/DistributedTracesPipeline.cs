// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class DistributedTracesPipeline : EventSourcePipeline<DistributedTracesPipelineSettings>
    {
        private readonly IActivityLogger[] _loggers;

        public DistributedTracesPipeline(DiagnosticsClient client,
            DistributedTracesPipelineSettings settings,
            IEnumerable<IActivityLogger> loggers) : base(client, settings)
        {
            _loggers = loggers?.ToArray() ?? throw new ArgumentNullException(nameof(loggers));
        }

        protected override MonitoringSourceConfiguration CreateConfiguration()
            => new ActivitySourceConfiguration(Settings.SamplingRatio, Settings.Sources);

        protected override async Task OnRun(CancellationToken token)
        {
            double samplingRatio = Settings.SamplingRatio;
            if (samplingRatio < 1D)
            {
                await ValidateEventSourceVersion().ConfigureAwait(false);
            }

            await base.OnRun(token).ConfigureAwait(false);
        }

        private async Task ValidateEventSourceVersion()
        {
            int majorVersion = 0;

            using CancellationTokenSource cancellationTokenSource = new();

            DiagnosticsEventPipeProcessor processor = new(
                new ActivitySourceConfiguration(1D, activitySourceNames: null),
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
                await processor.Process(Client, TimeSpan.FromSeconds(10), resumeRuntime: false, token: cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            await processor.DisposeAsync().ConfigureAwait(false);

            if (majorVersion < 9)
            {
                throw new PipelineException("Sampling ratio can only be set when listening to processes running System.Diagnostics.DiagnosticSource 9 or greater");
            }
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            await ExecuteActivityLoggerActionAsync((logger) => logger.PipelineStarted(token)).ConfigureAwait(false);

            eventSource.Dynamic.All += traceEvent => {
                try
                {
                    if (traceEvent.TryGetActivityPayload(out ActivityPayload activity))
                    {
                        foreach (IActivityLogger logger in _loggers)
                        {
                            try
                            {
                                logger.Log(
                                    in activity.ActivityData,
                                    activity.Tags);
                            }
                            catch (ObjectDisposedException)
                            {
                            }
                        }
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

            await ExecuteActivityLoggerActionAsync((logger) => logger.PipelineStopped(token)).ConfigureAwait(false);
        }

        private async Task ExecuteActivityLoggerActionAsync(Func<IActivityLogger, Task> action)
        {
            foreach (IActivityLogger logger in _loggers)
            {
                try
                {
                    await action(logger).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }
}
