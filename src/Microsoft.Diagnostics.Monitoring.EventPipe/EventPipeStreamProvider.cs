﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal sealed class EventPipeStreamProvider : IAsyncDisposable
    {
        private readonly MonitoringSourceConfiguration _sourceConfig;
        private readonly TaskCompletionSource<object> _stopProcessingSource;
        private Task _currentTask;

        public EventPipeStreamProvider(MonitoringSourceConfiguration sourceConfig)
        {
            _sourceConfig = sourceConfig;
            _stopProcessingSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task<Stream> ProcessEvents(DiagnosticsClient client, TimeSpan duration, bool resumeRuntime, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EventPipeSession session = null;
            try
            {
                IEnumerable<EventPipeProvider> providers = _sourceConfig.GetProviders();
                int bufferSizeInMB = _sourceConfig.BufferSizeInMB;
                long rundownKeyword = _sourceConfig.RundownKeyword;
                RetryStrategy retryStrategy = _sourceConfig.RetryStrategy;
                try
                {
                    EventPipeSessionConfiguration config = new(providers, bufferSizeInMB, rundownKeyword, true);
                    session = await client.StartEventPipeSessionAsync(config, cancellationToken).ConfigureAwait(false);
                }
                catch (UnsupportedCommandException) when (retryStrategy == RetryStrategy.DropKeywordKeepRundown)
                {
                    //
                    // If you are building new profiles or options, you can test with these asserts to make sure you are writing
                    // the retry strategies correctly.
                    //
                    // If these assert ever fires, it means something is wrong with the option generation logic leading to unnecessary retries.
                    // unnecessary retries is not fatal.
                    //
                    // Debug.Assert(rundownKeyword != 0);
                    // Debug.Assert(rundownKeyword != EventPipeSession.DefaultRundownKeyword);
                    //
                    EventPipeSessionConfiguration config = new(providers, bufferSizeInMB, EventPipeSession.DefaultRundownKeyword, true);
                    session = await client.StartEventPipeSessionAsync(config, cancellationToken).ConfigureAwait(false);
                }
                catch (UnsupportedCommandException) when (retryStrategy == RetryStrategy.DropKeywordDropRundown)
                {
                    //
                    // If you are building new profiles or options, you can test with these asserts to make sure you are writing
                    // the retry strategies correctly.
                    //
                    // If these assert ever fires, it means something is wrong with the option generation logic leading to unnecessary retries.
                    // unnecessary retries is not fatal.
                    //
                    // Debug.Assert(rundownKeyword != 0);
                    // Debug.Assert(rundownKeyword != EventPipeSession.DefaultRundownKeyword);
                    //
                    EventPipeSessionConfiguration config = new(providers, bufferSizeInMB, 0, true);
                    session = await client.StartEventPipeSessionAsync(config, cancellationToken).ConfigureAwait(false);
                }
                if (resumeRuntime)
                {
                    try
                    {
                        await client.ResumeRuntimeAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (UnsupportedCommandException)
                    {
                        // Noop if the command is unknown since the target process is most likely a 3.1 app.
                    }
                }
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidOperationException("End of stream", e);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException("Failed to start the event pipe session", ex);
            }

            _currentTask = Task.Run(async () => {
                using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                linkedSource.CancelAfter(duration);
                using CancellationTokenRegistration _ = linkedSource.Token.Register(() => _stopProcessingSource.TrySetResult(null));

                // Use TaskCompletionSource instead of Task.Delay with cancellation to avoid
                // using exceptions for normal termination of event stream.
                await _stopProcessingSource.Task.ConfigureAwait(false);

                await StopSessionAsync(session).ConfigureAwait(false);
            }, cancellationToken);

            return session.EventStream;
        }

        public void StopProcessing()
        {
            _stopProcessingSource.TrySetResult(null);
        }

        private static async Task StopSessionAsync(EventPipeSession session)
        {
            // Cancel after a generous amount of time if process ended before command is sent.
            using CancellationTokenSource cancellationSource = new(IpcClient.ConnectTimeout);
            try
            {
                await session.StopAsync(cancellationSource.Token).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                // If the app we're monitoring exits abruptly, this may throw in which case we just swallow the exception and exit gracefully.
            }
            // We may time out if the process ended before we sent StopTracing command. We can just exit in that case.
            catch (TimeoutException)
            {
            }
            // We may time out if the process ended before we sent StopTracing command. We can just exit in that case.
            catch (OperationCanceledException)
            {
            }
            // On Unix platforms, we may actually get a PNSE since the pipe is gone with the process, and Runtime Client Library
            // does not know how to distinguish a situation where there is no pipe to begin with, or where the process has exited
            // before collection started and got rid of a pipe that once existed.
            // Since we are catching this at the end of a session we know that the pipe once existed (otherwise the exception would've
            // been thrown at the beginning directly)
            catch (PlatformNotSupportedException)
            {
            }
            // On non-abrupt exits, the socket may be already closed by the runtime and we won't be able to send a stop request through it.
            catch (ServerNotAvailableException)
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
