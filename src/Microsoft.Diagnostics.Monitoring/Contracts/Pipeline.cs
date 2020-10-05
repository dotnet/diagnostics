// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Contracts
{
    /// <summary>
    /// Provides common functionality for pipelines, such as ensuring that Start/Stop tasks
    /// are idempotent and making sure Dispose calls properly cancel other operations
    /// </summary>
    public abstract class Pipeline : IPipeline, IAsyncDisposable
    {
        private readonly CancellationTokenSource _disposeSource = new CancellationTokenSource();
        private object _lock = new object();
        private bool _isDisposed;
        private Task _runTask;
        private Task _stopTask;
        private Task _abortTask;

        protected abstract Task OnRun(CancellationToken token);

        protected virtual Task OnAbort() => Task.CompletedTask;

        protected virtual Task OnStop(CancellationToken token) => Task.CompletedTask;

        protected virtual ValueTask OnDispose() => default;

        public Task RunAsync(CancellationToken token)
        {
            Task runTask = null;
            lock (_lock)
            {
                ThrowIfDisposed();

                if (_runTask == null)
                {
                    _runTask = RunAsyncCore(token);
                }
                runTask = _runTask;
            }
            return _runTask;
        }

        private async Task RunAsyncCore(CancellationToken token)
        {
            using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _disposeSource.Token))
            {
                try
                {
                    linkedSource.Token.ThrowIfCancellationRequested();
                    await OnRun(linkedSource.Token);
                }
                catch (OperationCanceledException)
                {
                    await Abort();
                    throw;
                }
            }
        }

        public Task StopAsync(CancellationToken token = default)
        {
            Task stopTask = null;
            lock (_lock)
            {
                ThrowIfDisposed();
                if (_runTask == null)
                {
                    throw new PipelineException("Unable to stop unstarted pipeline");
                }
                if (_stopTask == null)
                {
                    _stopTask = StopAsyncCore(token);
                }
                stopTask = _stopTask;
            }
            return stopTask;
        }

        private async Task StopAsyncCore(CancellationToken token)
        {
            using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(token, _disposeSource.Token))
            {
                try
                {
                    linkedSource.Token.ThrowIfCancellationRequested();
                    await OnStop(linkedSource.Token);
                }
                catch (OperationCanceledException)
                {
                    await Abort();
                    throw;
                }
            }
        }

        private async Task Abort()
        {
            Task abortTask = null;
            lock (_lock)
            {
                if (_abortTask == null)
                {
                    _abortTask = OnAbort();
                }
                abortTask = _abortTask;
            }
            await abortTask;
        }

        public async ValueTask DisposeAsync()
        {
            lock (_lock)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
            }
            _disposeSource.Cancel();

            //It's necessary to fully acquire the task, await it, and then move on to the next task.
            await SafeExecuteTask(() => _runTask);
            await SafeExecuteTask(() => _stopTask);
            await SafeExecuteTask(() => _abortTask);

            await OnDispose();
            _disposeSource.Dispose();
        }

        private async Task SafeExecuteTask(Func<Task> acquireTask)
        {
            Task task = null;
            lock (_lock)
            {
                task = acquireTask();
            }

            if (task != null)
            {
                try
                {
                    await task;
                }
                catch
                {
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
    }
}
