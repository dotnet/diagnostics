// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal abstract class EventSourcePipeline<T> : Pipeline where T : EventSourcePipelineSettings
    {
        private readonly Lazy<DiagnosticsEventPipeProcessor> _processor;
        public DiagnosticsClient Client { get; }
        public T Settings { get; }

        protected EventSourcePipeline(DiagnosticsClient client, T settings)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _processor = new Lazy<DiagnosticsEventPipeProcessor>(CreateProcessor);
        }

        internal virtual MonitoringSourceConfiguration CreateConfiguration()
        {
            return null;
        }

        internal virtual DiagnosticsEventPipeProcessor CreateProcessor()
        {
            MonitoringSourceConfiguration configuration = CreateConfiguration();
            if (null == configuration)
            {
                throw new ArgumentException(nameof(configuration));
            }

            return new DiagnosticsEventPipeProcessor(
                mode: PipeMode.EventSource,
                configuration: configuration,
                onEventSourceAvailable: OnEventSourceAvailable,
                onAfterProcess: OnAfterEventProcessing,
                onBeforeProcess: OnBeforeEventProcessing);
        }

        protected override Task OnRun(CancellationToken token)
        {
            try
            {
                return _processor.Value.Process(Client, Settings.Duration, token);
            }
            catch (InvalidOperationException e)
            {
                throw new PipelineException(e.Message, e);
            }
        }

        protected override async Task OnCleanup()
        {
            if (_processor.IsValueCreated)
            {
                await _processor.Value.DisposeAsync();
            }
            await base.OnCleanup();
        }

        protected override async Task OnStop(CancellationToken token)
        {
            if (_processor.IsValueCreated)
            {
                Task stoppingTask = _processor.Value.StopProcessing(token);

                var taskCompletionSource = new TaskCompletionSource<bool>();
                using IDisposable registration = token.Register(() => taskCompletionSource.SetCanceled());
                await Task.WhenAny(stoppingTask, taskCompletionSource.Task).Unwrap();
            }
        }

        protected virtual Task OnEventSourceAvailable(EventPipeEventSource eventSource, Func<Task> stopSessionAsync, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected virtual Task OnBeforeEventProcessing(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        protected virtual Task OnAfterEventProcessing(CancellationToken token)
        {
            return Task.CompletedTask;
        }
    }
}
