// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal partial class DiagnosticsEventPipeProcessor : IAsyncDisposable
    {
        private readonly MonitoringSourceConfiguration _configuration;
        private readonly Func<EventPipeEventSource, Func<Task>, CancellationToken, Task> _onEventSourceAvailable;

        private readonly object _lock = new object();

        private TaskCompletionSource<bool> _initialized;
        private TaskCompletionSource<bool> _sessionStarted;
        private EventPipeEventSource _eventSource;
        private Func<Task> _stopFunc;
        private bool _disposed;

        // Allows tests to know when the event pipe session has started so that the
        // target application can start producing events.
        internal Task SessionStarted => _sessionStarted.Task;

        public DiagnosticsEventPipeProcessor(
            MonitoringSourceConfiguration configuration,
            Func<EventPipeEventSource, Func<Task>, CancellationToken, Task> onEventSourceAvailable
            )
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _onEventSourceAvailable = onEventSourceAvailable ?? throw new ArgumentNullException(nameof(onEventSourceAvailable));

            _initialized = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sessionStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task Process(DiagnosticsClient client, TimeSpan duration, CancellationToken token)
        {
            //No need to guard against reentrancy here, since the calling pipeline does this already.
            IDisposable registration = token.Register(() => TryCancelCompletionSources(token));
            await await Task.Factory.StartNew(async () => {
                EventPipeEventSource source = null;
                EventPipeStreamProvider streamProvider = null;
                Task handleEventsTask = Task.CompletedTask;
                try
                {
                    streamProvider = new EventPipeStreamProvider(_configuration);
                    // Allows the event handling routines to stop processing before the duration expires.
                    Func<Task> stopFunc = () => Task.Run(() => { streamProvider.StopProcessing(); });

                    Stream sessionStream = await streamProvider.ProcessEvents(client, duration, token);

                    if (!_sessionStarted.TrySetResult(true))
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    source = new EventPipeEventSource(sessionStream);

                    handleEventsTask = _onEventSourceAvailable(source, stopFunc, token);

                    lock (_lock)
                    {
                        _eventSource = source;
                        _stopFunc = stopFunc;
                    }
                    registration.Dispose();
                    if (!_initialized.TrySetResult(true))
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    source.Process();
                    token.ThrowIfCancellationRequested();
                }
                catch (DiagnosticsClientException ex)
                {
                    InvalidOperationException wrappingException = new("Failed to start the event pipe session", ex);
                    TryFailCompletionSourcesReturnFalse(wrappingException);
                    throw wrappingException;
                }
                catch (Exception ex) when (TryFailCompletionSourcesReturnFalse(ex))
                {
                    throw;
                }
                finally
                {
                    registration.Dispose();
                    EventPipeEventSource eventSource = null;
                    lock (_lock)
                    {
                        eventSource = _eventSource;
                        _eventSource = null;
                    }

                    eventSource?.Dispose();
                    if (streamProvider != null)
                    {
                        await streamProvider.DisposeAsync();
                    }
                }

                // Await the task returned by the event handling method AFTER the EventPipeEventSource is disposed.
                // The EventPipeEventSource will only raise the Completed event when it is disposed. So if this task
                // is waiting for the Completed event to be raised, it will never complete until after EventPipeEventSource
                // is diposed.
                await handleEventsTask;

            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public async Task StopProcessing(CancellationToken token)
        {
            await _initialized.Task;

            EventPipeEventSource session = null;
            Func<Task> stopFunc = null;
            lock (_lock)
            {
                session = _eventSource;
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

            _initialized.TrySetCanceled();
            try
            {
                await _initialized.Task;
            }
            catch
            {
            }

            _sessionStarted.TrySetCanceled();

            _eventSource?.Dispose();
        }

        // Helper method for observing an exception while processing the trace session
        // so that session start task completion source can be failed and the exception handler
        // does not catch the exception.
        private bool TryFailCompletionSourcesReturnFalse(Exception ex)
        {
            // Use best-effort to set the completion sources to be cancelled or failed.
            if (ex is OperationCanceledException canceledException)
            {
                TryCancelCompletionSources(canceledException.CancellationToken);
            }
            else
            {
                _initialized.TrySetException(ex);
                _sessionStarted.TrySetException(ex);
            }

            // Return false to make the exception handler not handle the exception.
            return false;
        }

        private void TryCancelCompletionSources(CancellationToken token)
        {
            _initialized.TrySetCanceled(token);
            _sessionStarted.TrySetCanceled(token);
        }
    }
}
