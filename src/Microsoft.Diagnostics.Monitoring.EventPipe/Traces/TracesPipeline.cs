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
    internal class TracesPipeline : EventSourcePipeline<TracesPipelineSettings>
    {
        private readonly IActivityLogger[] _loggers;

        public TracesPipeline(DiagnosticsClient client,
            TracesPipelineSettings settings,
            IEnumerable<IActivityLogger> loggers) : base(client, settings)
        {
            _loggers = loggers?.ToArray() ?? throw new ArgumentNullException(nameof(loggers));
        }

        protected override MonitoringSourceConfiguration CreateConfiguration()
        {
            try
            {
                return new ActivitySourceConfiguration(Client, Settings.SamplingRatio, Settings.Sources);
            }
            catch (NotSupportedException ex)
            {
                throw new PipelineException(ex.Message, ex);
            }
        }

        protected override async Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            await ExecuteCounterLoggerActionAsync((logger) => logger.PipelineStarted(token)).ConfigureAwait(false);

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

            await ExecuteCounterLoggerActionAsync((logger) => logger.PipelineStopped(token)).ConfigureAwait(false);
        }

        private async Task ExecuteCounterLoggerActionAsync(Func<IActivityLogger, Task> action)
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
