// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticsMonitor = Microsoft.Diagnostics.Monitoring.EventPipe.DiagnosticsEventPipeProcessor.DiagnosticsMonitor;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal class EventTracePipeline : Pipeline
    {
        private readonly Lazy<DiagnosticsMonitor> _monitor;
        private readonly Func<Stream, CancellationToken, Task> _onStreamAvailable;

        public DiagnosticsClient Client { get; }
        public EventTracePipelineSettings Settings { get; }

        public EventTracePipeline(DiagnosticsClient client, EventTracePipelineSettings settings, Func<Stream, CancellationToken, Task> onStreamAvailable)
        {
            Client = client;
            Settings = settings;
            _onStreamAvailable = onStreamAvailable ?? throw new ArgumentNullException(nameof(onStreamAvailable));
            _monitor = new Lazy<DiagnosticsMonitor>(CreateMonitor);
        }

        protected override async Task OnRun(CancellationToken token)
        {
            try
            {
                Stream eventStream = await _monitor.Value.ProcessEvents(Client, Settings.Duration, token);

                await _onStreamAvailable(eventStream, token);
            }
            catch (InvalidOperationException e)
            {
                throw new PipelineException(e.Message, e);
            }
        }

        protected override async Task OnCleanup()
        {
            if (_monitor.IsValueCreated)
            {
                await _monitor.Value.DisposeAsync();
            }
            await base.OnCleanup();
        }

        protected override Task OnStop(CancellationToken token)
        {
            if (_monitor.IsValueCreated)
            {
                _monitor.Value.StopProcessing();
            }
            return Task.CompletedTask;
        }

        private DiagnosticsMonitor CreateMonitor()
        {
            return new DiagnosticsMonitor(Settings.Configuration);
        }
    }
}
