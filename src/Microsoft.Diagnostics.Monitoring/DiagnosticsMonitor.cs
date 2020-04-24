// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class DiagnosticsMonitor : IAsyncDisposable
    {
        private readonly IServiceProvider _services;
        private readonly Microsoft.Extensions.Logging.ILogger<DiagnosticsMonitor> _logger;
        private readonly MonitoringSourceConfiguration _sourceConfig;
        private readonly CancellationTokenSource _disposeSource;
        private readonly object _lock = new object();
        private Task _currentTask;
        private bool _disposed;

        public DiagnosticsMonitor(IServiceProvider services, MonitoringSourceConfiguration sourceConfig)
        {
            _services = services;
            _sourceConfig = sourceConfig;
            _logger = _services.GetService<ILogger<DiagnosticsMonitor>>();
            _disposeSource = new CancellationTokenSource();
        }

        public Task CurrentProcessingTask => _currentTask;

        public Task<Stream> ProcessEvents(int processId, int duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(DiagnosticsMonitor));
                }

                if (_currentTask != null)
                {
                    throw new InvalidOperationException("Only one stream processing is allowed");
                }

                EventPipeSession session = null;
                var client = new DiagnosticsClient(processId);

                try
                {
                    session = client.StartEventPipeSession(_sourceConfig.GetProviders());
                }
                catch (EndOfStreamException e)
                {
                    throw new InvalidOperationException("End of stream", e);
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug(0, ex, "Failed to start the event pipe session");
                    }
                    throw new InvalidOperationException(ex.Message, ex);
                }

                CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_disposeSource.Token, cancellationToken);

                _currentTask = Task.Run( async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(duration), linkedSource.Token);
                    }
                    finally
                    {
                        linkedSource.Dispose();
                        StopSession(session);
                    }
                });

                return Task.FromResult(session.EventStream);
            }
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
            if (_disposed)
            {
                return;
            }

            Task currentTask = null;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                currentTask = _currentTask;
                _currentTask = null;
                _disposed = true;
            }
             _disposeSource.Cancel();
            if (currentTask != null)
            {
                await currentTask;
            }
            _disposeSource?.Dispose();
        }
    }
}