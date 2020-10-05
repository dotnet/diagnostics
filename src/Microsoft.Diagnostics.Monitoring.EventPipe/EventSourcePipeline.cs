// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Graphs;
using Microsoft.Diagnostics.Monitoring.Contracts;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public abstract class EventSourcePipeline<T> : Pipeline where T : EventSourcePipelineSettings
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

        internal abstract DiagnosticsEventPipeProcessor CreateProcessor();

        protected override Task OnRun(CancellationToken token)
        {
            return _processor.Value.Process(Client, Settings.ProcessId, Settings.Duration, token);
        }

        protected override ValueTask OnDispose()
        {
            if (_processor.IsValueCreated)
            {
                return _processor.Value.DisposeAsync();
            }
            return default;
            
        }

        protected override Task OnStop(CancellationToken token)
        {
            if (_processor.IsValueCreated)
            {
                Task stoppingTask = _processor.Value.StopProcessing(token);

                var taskCompletionSource = new TaskCompletionSource<bool>();
                var src = new TaskCompletionSource<T>();
                token.Register(() => src.SetCanceled());
                return Task.WhenAny(stoppingTask, src.Task).Unwrap();
            }
            return Task.CompletedTask;
        }
    }
}
