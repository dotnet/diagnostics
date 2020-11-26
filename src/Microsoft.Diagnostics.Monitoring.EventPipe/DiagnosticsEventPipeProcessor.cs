// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal partial class DiagnosticsEventPipeProcessor : IAsyncDisposable
    {
        private readonly PipeMode _mode;
        private readonly MonitoringSourceConfiguration _userConfig;
        private readonly Func<Stream, CancellationToken, Task> _onStreamAvailable;
        private readonly Func<EventPipeEventSource, Func<Task>, CancellationToken, Task> _onEventSourceAvailable;
        private readonly Func<CancellationToken, Task> _onAfterProcess;
        private readonly Func<CancellationToken, Task> _onBeforeProcess;

        private readonly object _lock = new object();

        private TaskCompletionSource<bool> _sessionStarted;
        private EventPipeEventSource _eventPipeSession;
        private Func<Task> _stopFunc;
        private bool _disposed;

        public DiagnosticsEventPipeProcessor(
            PipeMode mode,
            MonitoringSourceConfiguration configuration = null, // PipeMode = Nettrace, EventSource
            Func<Stream, CancellationToken, Task> onStreamAvailable = null, // PipeMode = Nettrace
            Func<EventPipeEventSource, Func<Task>, CancellationToken, Task> onEventSourceAvailable = null, // PipeMode = EventSource
            Func<CancellationToken, Task> onAfterProcess = null, // PipeMode = EventSource
            Func<CancellationToken, Task> onBeforeProcess = null // PipeMode = EventSource
            )
        {
            _mode = mode;
            _userConfig = configuration;
            _onStreamAvailable = onStreamAvailable;

            _onEventSourceAvailable = onEventSourceAvailable;
            _onAfterProcess = onAfterProcess;
            _onBeforeProcess = onBeforeProcess;

            _sessionStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task Process(DiagnosticsClient client, TimeSpan duration, CancellationToken token)
        {
            //No need to guard against reentrancy here, since the calling pipeline does this already.
            IDisposable registration = token.Register(() => _sessionStarted.TrySetCanceled());
            await await Task.Factory.StartNew(async () =>
            {
                EventPipeEventSource source = null;
                DiagnosticsMonitor monitor = null;
                Task handleEventsTask = Task.CompletedTask;
                try
                {
                    MonitoringSourceConfiguration config = null;
                    if (_mode == PipeMode.Nettrace || _mode == PipeMode.EventSource)
                    {
                        config = _userConfig;
                    }

                    monitor = new DiagnosticsMonitor(config);
                    // Allows the event handling routines to stop processing before the duration expires.
                    Func<Task> stopFunc = () => Task.Run(() => { monitor.StopProcessing(); });

                    Stream sessionStream = await monitor.ProcessEvents(client, duration, token);

                    if (_mode == PipeMode.Nettrace)
                    {
                        if (!_sessionStarted.TrySetResult(true))
                        {
                            token.ThrowIfCancellationRequested();
                        }

                        lock (_lock)
                        {
                            //Save the stop function for later, so that we can stop a trace later.
                            _stopFunc = stopFunc;
                        }

                        await _onStreamAvailable(sessionStream, token);
                        return;
                    }

                    source = new EventPipeEventSource(sessionStream);

                    if (_mode == PipeMode.EventSource)
                    {
                        await _onEventSourceAvailable(source, stopFunc, token);
                    }

                    lock(_lock)
                    {
                        _eventPipeSession = source;
                        _stopFunc = stopFunc;
                    }
                    registration.Dispose();
                    if (!_sessionStarted.TrySetResult(true))
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    if (_mode == PipeMode.EventSource)
                    {
                        await _onBeforeProcess?.Invoke(token);
                    }

                    source.Process();
                    token.ThrowIfCancellationRequested();
                }
                catch (DiagnosticsClientException ex)
                {
                    throw new InvalidOperationException("Failed to start the event pipe session", ex);
                }
                finally
                {
                    registration.Dispose();
                    EventPipeEventSource session = null;
                    lock (_lock)
                    {
                        session = _eventPipeSession;
                        _eventPipeSession = null;
                    }

                    session?.Dispose();
                    if (monitor != null)
                    {
                        await monitor.DisposeAsync();
                    }
                }

                // Await the task returned by the event handling method AFTER the EventPipeEventSource is disposed.
                // The EventPipeEventSource will only raise the Completed event when it is disposed. So if this task
                // is waiting for the Completed event to be raised, it will never complete until after EventPipeEventSource
                // is diposed.
                await handleEventsTask;

                if (_mode == PipeMode.EventSource)
                {
                    await _onAfterProcess?.Invoke(token);
                }

            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public async Task StopProcessing(CancellationToken token)
        {
            await _sessionStarted.Task;

            EventPipeEventSource session = null;
            Func<Task> stopFunc = null;
            lock (_lock)
            {
                session = _eventPipeSession;
                stopFunc = _stopFunc;
            }
            if (session != null)
            {
                //TODO This API is not sufficient to stop data flow.
                session.StopProcessing();
            }
            if (stopFunc != null)
            {
                await stopFunc();
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }

            _sessionStarted.TrySetCanceled();
            try
            {
                await _sessionStarted.Task;
            }
            catch
            {
            }

            _eventPipeSession?.Dispose();
        }
    }
}
