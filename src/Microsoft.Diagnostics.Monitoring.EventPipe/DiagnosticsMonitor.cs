// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    partial class DiagnosticsEventPipeProcessor
    {
        private sealed class DiagnosticsMonitor : IAsyncDisposable
        {
            private readonly MonitoringSourceConfiguration _sourceConfig;
            private readonly TaskCompletionSource<object> _stopProcessingSource;
            private Task _currentTask;

            public DiagnosticsMonitor(MonitoringSourceConfiguration sourceConfig)
            {
                _sourceConfig = sourceConfig;
                _stopProcessingSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Task<Stream> ProcessEvents(DiagnosticsClient client, TimeSpan duration, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                EventPipeSession session = null;
                try
                {
                    session = client.StartEventPipeSession(_sourceConfig.GetProviders(), _sourceConfig.RequestRundown, _sourceConfig.BufferSizeInMB);
                }
                catch (EndOfStreamException e)
                {
                    throw new InvalidOperationException("End of stream", e);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    throw new InvalidOperationException("Failed to start the event pipe session", ex);
                }

                _currentTask = Task.Run(async () =>
                {
                    using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linkedSource.CancelAfter(duration);
                    using var _ = linkedSource.Token.Register(() => _stopProcessingSource.TrySetResult(null));

                    // Use TaskCompletionSource instead of Task.Delay with cancellation to avoid
                    // using exceptions for normal termination of event stream.
                    await _stopProcessingSource.Task.ConfigureAwait(false);
                    StopSession(session);
                });

                return Task.FromResult(session.EventStream);
            }

            public void StopProcessing()
            {
                _stopProcessingSource.TrySetResult(null);
            }

            private static void StopSession(EventPipeSession session)
            {
                try
                {
                    session.Stop();
                }
                catch (EndOfStreamException)
                {
                    // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
                }
                // We may time out if the process ended before we sent StopTracing command. We can just exit in that case.
                catch (TimeoutException)
                {
                }
                // On Unix platforms, we may actually get a PNSE since the pipe is gone with the process, and Runtime Client Library
                // does not know how to distinguish a situation where there is no pipe to begin with, or where the process has exited
                // before dotnet-counters and got rid of a pipe that once existed.
                // Since we are catching this in StopMonitor() we know that the pipe once existed (otherwise the exception would've 
                // been thrown in StartMonitor directly)
                catch (PlatformNotSupportedException)
                {
                }
            }

            public async ValueTask DisposeAsync()
            {
                Task currentTask = _currentTask;
                _stopProcessingSource.TrySetResult(null);
                if (currentTask != null)
                {
                    try
                    {
                        await currentTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }
    }
}