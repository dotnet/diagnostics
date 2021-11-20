// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// A pipeline controls data which is flowing from some source to sink asynchronously.
    /// This class allows the flow to be started and stopped. The concrete class
    /// determines what data is being collected and where it will flow to.
    ///
    /// The pipeline is logically in one of these states:
    /// Unstarted - After the object is constructed and prior to calling RunAsync or
    /// StopAsync. No data is flowing.
    /// Running - The pipeline is doing whatever asynchronous work is necessary to flow
    /// data. Unstarted transitions to Running with a call to RunAsync()
    /// Stopping - The pipeline is doing a graceful shutdown to stop receiving any new
    /// data and drain any in-flight data to the sink. Unstarted or Running transitions to
    /// Stopping with a call to StopAsync(). Pipelines may also automatically enter a Stopping
    /// state when there is no data left to receive from the source.
    /// Stopped - All asynchronous work has ceased and the pipeline can not be restarted. This
    /// transition happens asynchronously from the stopping state when there is no
    /// work left to be done. The only way to be certain you have reached this state is to
    /// observe that the Task returned by StopAsync() or RunAsync() is completed or cancelled,
    /// usually by awaiting it.
    /// Aborting - Entered by cancelling any of the tokens to StopAsync or RunAsync, or by explicitly
    /// calling DisposeAsync.
    /// Unstarted -> Running -> Stopping -> Stopped
    ///           |           |               ^
    ///           |           V               |
    ///           -------> Aborting ----------|
    /// </summary>
    internal abstract class Pipeline : IAsyncDisposable
    {
        private readonly CancellationTokenSource _disposeSource = new CancellationTokenSource();
        private object _lock = new object();
        private bool _isCleanedUp;
        private Task _runTask;
        private Task _stopTask;
        private Task _cleanupTask;

        protected abstract Task OnRun(CancellationToken token);

        protected virtual Task OnCleanup() => Task.CompletedTask;

        protected virtual Task OnStop(CancellationToken token) => Task.CompletedTask;

        /// <summary>
        /// Causes an unstarted pipeline to start running, which makes data flow from source
        /// to sink. Calling this more than once doesn't have any additional effect and returns
        /// the same Task. Once the pipeline transitions to the Stopped state the returned Task
        /// will be complete or cancelled.
        /// </summary>
        /// <param name="token">If this token is cancelled, it signals the pipeline to abandon all data transfer
        /// operations as quickly as possible.
        /// </param>
        /// <exception cref="PipelineException">For any error that prevents all the requested
        /// data from being moved through the pipeline</exception>
        /// <remarks>Any exception other than PipelineException represents either
        /// a bug in the pipeline implementation because it was unanticipated or a failure in
        /// lower level runtime/OS/hardware to keep the process in a consistent state</remarks>
        public Task RunAsync(CancellationToken token)
        {
            Task runTask = null;
            lock (_lock)
            {
                if (_isCleanedUp)
                {
                    runTask = _runTask ?? Task.CompletedTask;
                }
                else
                {
                    if (_runTask == null)
                    {
                        _runTask = RunAsyncCore(token);
                    }
                    runTask = _runTask;
                }
            }
            return runTask;
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
                    //Give precedence to the parameter token rather than the linked token
                    token.ThrowIfCancellationRequested();
                    throw;
                }
                finally
                {
                    await Cleanup();
                }
            }
        }

        /// <summary>
        /// Causes an unstarted or running pipeline to transition to the stopping state. In this
        /// state data flow from the source will be stopped and any in-flight data is gracefully
        /// drained. Calling this more than once doesn't have any additional effect and returns
        /// the same Task. Once the pipeline transitions to the Stopped state the returned Task
        /// will be complete or cancelled.
        /// </summary>
        /// <param name="cancelToken">If this token is cancelled, it aborts the operation without consideration for
        /// preserving the data</param>
        /// <exception cref="PipelineException">For any error that prevents all the requested
        /// data from being moved through the pipeline</exception>
        /// <remarks>Any exception other than PipelineException represents either
        /// a bug in the pipeline implementation because it was unanticipated or a failure in
        /// lower level runtime/OS/hardware to keep the process in a consistent state</remarks>
        public Task StopAsync(CancellationToken token = default)
        {
            Task stopTask = null;
            lock (_lock)
            {
                if (_isCleanedUp)
                {
                    stopTask = _stopTask ?? Task.CompletedTask;
                }
                else if (_runTask == null)
                {
                    throw new PipelineException("Unable to stop unstarted pipeline");
                }
                else
                {
                    if (_stopTask == null)
                    {
                        _stopTask = StopAsyncCore(token);
                    }
                    stopTask = _stopTask;
                }
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
                    await Cleanup();
                    //Give precedence to the parameter token rather than the linked token
                    token.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }

        private Task Cleanup()
        {
            Task cleanupTask = null;
            lock (_lock)
            {
                if (_cleanupTask == null)
                {
                    _cleanupTask = OnCleanup();
                }
                cleanupTask = _cleanupTask;
            }
            return cleanupTask;
        }

        /// <summary>
        /// Requests that the pipeline abort the data flow as quickly as possible and transitions
        /// to Stopped state. Note that this will not cause the pipeline to trigger ObjectDisposedException.
        /// </summary>
        /// <returns></returns>
        public async ValueTask DisposeAsync()
        {
            lock (_lock)
            {
                if (_isCleanedUp)
                {
                    return;
                }
                _isCleanedUp = true;
            }
            _disposeSource.Cancel();

            //It's necessary to fully acquire the task, await it, and then move on to the next task.
            await SafeExecuteTask(() => _runTask);
            await SafeExecuteTask(() => _stopTask);
            await SafeExecuteTask(() => _cleanupTask);

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
    }
}
