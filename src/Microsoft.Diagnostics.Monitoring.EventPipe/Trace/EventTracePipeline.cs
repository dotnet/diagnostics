// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventTracePipeline : Pipeline
    {
        private readonly Lazy<EventPipeStreamProvider> _provider;
        private readonly Func<Stream, CancellationToken, Task> _onStreamAvailable;

        public DiagnosticsClient Client { get; }
        public EventTracePipelineSettings Settings { get; }

        public EventTracePipeline(DiagnosticsClient client, EventTracePipelineSettings settings, Func<Stream, CancellationToken, Task> onStreamAvailable)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _onStreamAvailable = onStreamAvailable ?? throw new ArgumentNullException(nameof(onStreamAvailable));
            _provider = new Lazy<EventPipeStreamProvider>(CreateProvider);
        }

        protected override async Task OnRun(CancellationToken token)
        {
            try
            {
                //It is important that the underlying stream be completely read, or disposed.
                //If rundown is enabled, the underlying stream must be drained or disposed, or the app hangs.
                using Stream eventStream = await _provider.Value.ProcessEvents(Client, Settings.Duration, Settings.ResumeRuntime, token).ConfigureAwait(false);

                await _onStreamAvailable(eventStream, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException e)
            {
                throw new PipelineException(e.Message, e);
            }
        }

        protected override async Task OnCleanup()
        {
            if (_provider.IsValueCreated)
            {
                await _provider.Value.DisposeAsync().ConfigureAwait(false);
            }
            await base.OnCleanup().ConfigureAwait(false);
        }

        protected override Task OnStop(CancellationToken token)
        {
            if (_provider.IsValueCreated)
            {
                _provider.Value.StopProcessing();
            }
            return Task.CompletedTask;
        }

        private EventPipeStreamProvider CreateProvider()
        {
            return new EventPipeStreamProvider(Settings.Configuration);
        }
    }
}
