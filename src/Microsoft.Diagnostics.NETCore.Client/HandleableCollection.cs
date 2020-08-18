// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// A collection of objects that allows for observability and mutability using handlers.
    /// </summary>
    internal class HandleableCollection<T> : IEnumerable<T>, IDisposable
    {
        public delegate bool Handler(in T item, out bool removeItem);

        public delegate bool Predicate(in T item);

        /// <summary>
        /// Accepts the first item it encounters and requests that the item is removed from the collection.
        /// </summary>
        private static readonly Handler DefaultHandler = (in T item, out bool removeItem) => { removeItem = true; return true; };

        private readonly CancellationTokenSource _disposalSource = new CancellationTokenSource();
        private readonly List<T> _items = new List<T>();
        private readonly List<Tuple<TaskCompletionSource<T>, Handler>> _handlers = new List<Tuple<TaskCompletionSource<T>, Handler>>();

        private bool _disposed = false;

        /// <summary>
        /// Returns an enumerator that iterates through the underlying collection.
        /// </summary>
        /// <remarks>
        /// The returned enumerator is over a copy of the underlying collection so that there are no concurrency issues.
        /// </remarks>
        IEnumerator IEnumerable.GetEnumerator()
        {
            VerifyNotDisposed();

            IList<T> copy;
            lock (_items)
            {
                copy = _items.ToList();
            }
            return copy.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the underlying collection.
        /// </summary>
        /// <remarks>
        /// The returned enumerator is over a copy of the underlying collection so that there are no concurrency issues.
        /// </remarks>
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            VerifyNotDisposed();

            IList<T> copy;
            lock (_items)
            {
                copy = _items.ToList();
            }
            return copy.GetEnumerator();
        }

        /// <summary>
        /// Disposes the <see cref="HandleableCollection{T}"/> by clearing all items and handlers.
        /// </summary>
        /// <remarks>
        /// All pending handlers with throw <see cref="ObjectDisposedException"/>.
        /// </remarks>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposalSource.Cancel();

                lock (_items)
                {
                    _items.Clear();

                    _handlers.Clear();
                }

                _disposalSource.Dispose();

                _disposed = true;
            }
        }

        /// <summary>
        /// Adds an item so that it may be observed by a handler.
        /// </summary>
        /// <param name="item">Item to add to the collection.</param>
        /// <remarks>
        /// The item may be immediately consumed if a handler removes the item, thus it may
        /// not be stored into the underlying list.
        /// </remarks>
        public void Add(in T item)
        {
            VerifyNotDisposed();

            lock (_items)
            {
                bool handledValue = false;
                for (int i = 0; !handledValue && i < _handlers.Count; i++)
                {
                    Tuple<TaskCompletionSource<T>, Handler> handler = _handlers[i];

                    if (TryHandler(item, handler.Item2, handler.Item1, out handledValue))
                    {
                        _handlers.RemoveAt(i);
                        i--;
                    }
                }

                if (!handledValue)
                {
                    _items.Add(item);
                }
            }
        }

        /// <summary>
        /// Returns the first item offered to the handler
        /// or waits for a future item if no item is immediately available.
        /// </summary>
        /// <param name="timeout">The amount of time to wait before cancelling the handler.</param>
        /// <returns>The first item offered to the handler.</returns>
        public T Handle(TimeSpan timeout)
        {
            VerifyNotDisposed();

            return Handle(DefaultHandler, timeout);
        }

        /// <summary>
        /// Returns the item on which the handler completes or waits for future items
        /// if the handler does not immediately complete.
        /// </summary>
        /// <param name="handler">The handler that determines on which item to complete.</param>
        /// <param name="timeout">The amount of time to wait before cancelling the handler.</param>
        /// <returns>The item on which the handler completes.</returns>
        public T Handle(Handler handler, TimeSpan timeout)
        {
            VerifyNotDisposed();

            using var cancellation = new CancellationTokenSource(timeout);
            Task<T> handleTask = HandleAsync(
                handler,
                tcs => tcs.TrySetException(new TimeoutException()),
                cancellation.Token);

            try
            {
                return handleTask.Result;
            }
            catch (AggregateException ex)
            {
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Returns the first item offered to the handler
        /// or waits for a future item if no item is immediately available.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with the first item offered to the handler.</returns>
        public Task<T> HandleAsync(CancellationToken token)
        {
            VerifyNotDisposed();

            return HandleAsync(DefaultHandler, token);
        }

        /// <summary>
        /// Returns the item on which the handler completes and waits for future items
        /// if the handler does not immediately complete.
        /// </summary>
        /// <param name="handler">The handler that determines on which item to complete.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with the item on which the handler completes.</returns>
        public Task<T> HandleAsync(Handler handler, CancellationToken token)
        {
            VerifyNotDisposed();

            return HandleAsync(
                handler,
                tcs => tcs.TrySetCanceled(token),
                token);
        }

        private async Task<T> HandleAsync(Handler handler, Action<TaskCompletionSource<T>> tokenCallback, CancellationToken token)
        {
            var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var methodRegistration = token.Register(() => tokenCallback(completionSource));
            using var disposalRegistration = _disposalSource.Token.Register(
                () => completionSource.TrySetException(new ObjectDisposedException(nameof(HandleableCollection<T>))));

            lock (_items)
            {
                OnHandlerBegin();

                bool stopHandling = false;
                for (int i = 0; !stopHandling && i < _items.Count; i++)
                {
                    T item = _items[i];

                    stopHandling = TryHandler(item, handler, completionSource, out bool removeItem);

                    if (removeItem)
                    {
                        _items.RemoveAt(i);
                        i--;
                    }
                }

                if (!stopHandling)
                {
                    _handlers.Add(Tuple.Create(completionSource, handler));
                }
            }

            return await completionSource.Task.ConfigureAwait(false);
        }

        private static bool TryHandler(in T item, Handler handler, TaskCompletionSource<T> completionSource, out bool removeItem)
        {
            removeItem = false;
            if (completionSource.Task.IsCompleted)
            {
                return true;
            }

            bool stopHandling = handler(item, out removeItem);
            if (stopHandling)
            {
                completionSource.TrySetResult(item);
            }

            return stopHandling;
        }

        /// <summary>
        /// Attempts to remove the first item that satisfies the <paramref name="predicate"/>.
        /// </summary>
        /// <param name="predicate">A predicate to determine which item should be removed.</param>
        /// <param name="oldItem">If the predicate is satisfied, the item that is removed.</param>
        /// <returns>True if an item satifies the predicate; otherwise false.</returns>
        public bool TryRemove(Predicate predicate, out T oldItem)
        {
            VerifyNotDisposed();

            lock (_items)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (predicate(_items[i]))
                    {
                        oldItem = _items[i];
                        _items.RemoveAt(i);
                        return true;
                    }
                }
            }

            oldItem = default;
            return false;
        }

        /// <summary>
        /// Attempts to replace the first item that satisfies the <paramref name="predicate"/>.
        /// </summary>
        /// <param name="predicate">A predicate to determine which item should be replaced.</param>
        /// <param name="newItem">The new item that should replace the item that satisfies the predicate.</param>
        /// <param name="oldItem">If the predicate is satisfied, the item that is removed.</param>
        /// <returns>True if an item satifies the predicate; otherwise false.</returns>
        public bool TryReplace(Predicate predicate, in T newItem, out T oldItem)
        {
            VerifyNotDisposed();

            lock (_items)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (predicate(_items[i]))
                    {
                        oldItem = _items[i];
                        _items[i] = newItem;
                        return true;
                    }
                }
            }

            oldItem = default;
            return false;
        }

        protected void VerifyNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HandleableCollection<T>));
            }
        }

        protected virtual void OnHandlerBegin()
        {
        }
    }
}
