﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
                Stream eventStream = await _provider.Value.ProcessEvents(Client, Settings.Duration, token);

                await _onStreamAvailable(eventStream, token);
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
                await _provider.Value.DisposeAsync();
            }
            await base.OnCleanup();
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
