// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    // Provides a mechanism to allow an event handler to signal the completion of a Task
    // that represents the raising of the event. The event handler may complete the task
    // under its own defined condition rather than unconditionally complete the task when
    // the event is raised.
    internal class EventTaskSource<THandler> : IDisposable
    {
        private IDisposable _cancellationRegistration;
        private THandler _completeHandler;
        private TaskCompletionSource<object> _completionSource;
        private Action<THandler> _removeHandler;

        /// <param name="createHandler">Function that creates an event handler to subscribe to the target event.
        /// Receives an Action that is used to complete the task of this task source.</param>
        /// <param name="addHandler">Action that subscribes the created event handler to to target event.</param>
        /// <param name="removeHandler">Action that unsubscribes the created event handler to to target event.</param>
        /// <param name="token">Cancels the Task provided by this task source, if it has not completed.</param>
        public EventTaskSource(
            Func<Action, THandler> createHandler,
            Action<THandler> addHandler,
            Action<THandler> removeHandler,
            CancellationToken token = default)
        {
            _completionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Register a cancellation callback to attempt to cancel the task completion source.
            if (token != default)
            {
                _cancellationRegistration = token.Register(() => _completionSource.TrySetCanceled(token));
            }

            // Call the createHandler func with an annonymous func that transitions the task
            // completion source to the RanToCompletion state.
            _completeHandler = createHandler(() => _completionSource.TrySetResult(null));

            // Invoke addHandler so that the caller can subscribe the handler in order to start
            // receiving event raising notifications.
            addHandler(_completeHandler);

            _removeHandler = removeHandler;
        }

        public void Dispose()
        {
            // Clear cancellation registration
            _cancellationRegistration?.Dispose();
            _cancellationRegistration = null;

            // Remove event handler
            _removeHandler?.Invoke(_completeHandler);
            _removeHandler = null;

            // Set task to cancelled state if not already in final state
            _completionSource.TrySetCanceled();
        }

        public Task Task => _completionSource.Task;
    }
}
