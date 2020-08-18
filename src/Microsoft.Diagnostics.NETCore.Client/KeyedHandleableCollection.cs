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
    /// A collection of objects with keys that allows for observability and mutability using handlers.
    /// </summary>
    internal class KeyedHandleableCollection<TKey, TValue> :
        HandleableCollection<Tuple<TKey, TValue>>
    {
        public delegate bool KeyedHandler(in TValue value, out bool removeItem);

        /// <summary>
        /// Accepts the value it encounters and requests that the value is removed from the collection.
        /// </summary>
        private static readonly KeyedHandler DefaultHandler = (in TValue value, out bool removeItem) => { removeItem = true; return true; };

        /// <summary>
        /// Adds the specified value using the key as a reference to the value.
        /// </summary>
        /// <param name="key">The key of the value to add.</param>
        /// <param name="value">The value to be added. Can be null.</param>
        public void Add(TKey key, TValue value)
        {
            VerifyNotDisposed();

            if (null == key)
            {
                throw new ArgumentNullException(nameof(key));
            }

            // CONSIDER: Check that the key does not already exist.
            Add(Tuple.Create(key, value));
        }

        /// <summary>
        /// Returns the value with the corresponding <paramref name="key"/> offered to the handler
        /// or waits for a future item if no item with the corresponding <paramref name="key"/> is immediately available.
        /// </summary>
        /// <param name="key">The key to locate in the underlying collection.</param>
        /// <param name="timeout">The amount of time to wait before cancelling the handler.</param>
        /// <returns>The value with the corresponding <paramref name="key"/>.</returns>
        public TValue Handle(TKey key, TimeSpan timeout)
        {
            VerifyNotDisposed();

            if (null == key)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return Handle(key, DefaultHandler, timeout);
        }

        /// <summary>
        /// Returns the value with the corresponding <paramref name="key"/> on which the handler completes
        /// or waits for a future value with the corresponding <paramref name="key"/> if the handler does not immediately complete.
        /// </summary>
        /// <param name="key">The key to locate in the underlying collection.</param>
        /// <param name="handler">The handler that determines on which item to complete.</param>
        /// <param name="timeout">The amount of time to wait before cancelling the handler.</param>
        /// <returns>The value with the corresponding <paramref name="key"/> on which the handler completes.</returns>
        public TValue Handle(TKey key, KeyedHandler handler, TimeSpan timeout)
        {
            VerifyNotDisposed();

            if (null == key)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var result = Handle(CreateKeyedHandler(key, handler), timeout);

            return result.Item2;
        }

        /// <summary>
        /// Returns the value with the corresponding <paramref name="key"/> offered to the handler
        /// or waits for a future item if no item with the corresponding <paramref name="key"/> is immediately available.
        /// </summary>
        /// <param name="key">The key to locate in the underlying collection.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with the value with the corresponding <paramref name="key"/>.</returns>
        public Task<TValue> HandleAsync(TKey key, CancellationToken token)
        {
            VerifyNotDisposed();

            if (null == key)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return HandleAsync(key, DefaultHandler, token);
        }

        /// <summary>
        /// Returns the value with the corresponding <paramref name="key"/> on which the handler completes
        /// or waits for a future value with the corresponding <paramref name="key"/> if the handler does not immediately complete.
        /// </summary>
        /// <param name="key">The key to locate in the underlying collection.</param>
        /// <param name="handler">The handler that determines on which item to complete.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with the value with the corresponding <paramref name="key"/> on which the handler completes.</returns>
        public async Task<TValue> HandleAsync(TKey key, KeyedHandler handler, CancellationToken token)
        {
            VerifyNotDisposed();

            if (null == key)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var result = await HandleAsync(CreateKeyedHandler(key, handler), token);

            return result.Item2;
        }

        /// <summary>
        /// Attempts to remove the value with the corresponding <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to locate in the underlying collection.</param>
        /// <param name="oldValue">The value with the corresponding <paramref name="key"/> if the key is found.</param>
        /// <returns>True if a value has the corresponding <paramref name="key"/>; otherwise false.</returns>
        public bool TryRemove(TKey key, out TValue oldValue)
        {
            VerifyNotDisposed();

            if (null == key)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (TryRemove(
                (in Tuple<TKey, TValue> t) => t.Item1.Equals(key),
                out Tuple<TKey, TValue> oldItem))
            {
                oldValue = oldItem.Item2;
                return true;
            }

            oldValue = default;
            return false;
        }

        /// <summary>
        /// Attempts to replace the value with the corresponding <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to locate in the underlying collection.</param>
        /// <param name="newValue">The new value to replace the existing value with the corresponding <paramref name="key"/>.</param>
        /// <param name="oldValue">The value with the corresponding <paramref name="key"/> that was replaced if the key is found.</param>
        /// <returns>True if a value has the corresponding <paramref name="key"/>; otherwise false.</returns>
        public bool TryReplace(TKey key, TValue newValue, out TValue oldValue)
        {
            VerifyNotDisposed();

            if (null == key)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (TryReplace(
                (in Tuple<TKey, TValue> t) => t.Item1.Equals(key),
                Tuple.Create(key, newValue),
                out Tuple<TKey, TValue> previous))
            {
                oldValue = previous.Item2;
                return true;
            }

            oldValue = default;
            return false;
        }

        private Handler CreateKeyedHandler(TKey key, KeyedHandler handler)
        {
            // Create a handler that considers the key and only allows forwarding to the wrapped
            // handler if the key matches the key portion of the item.
            return (in Tuple<TKey, TValue> tuple, out bool removeItem) =>
            {
                if (!tuple.Item1.Equals(key))
                {
                    removeItem = false;
                    return false;
                }

                return handler(tuple.Item2, out removeItem);
            };
        }

        public IEnumerable<TValue> Values
        {
            get
            {
                VerifyNotDisposed();

                return this.Select(tuple => tuple.Item2);
            }
        }

        /// <summary>
        /// An enumerator that returns the values for each item in the underlying collection.
        /// </summary>
        private class Enumerator : IEnumerator<TValue>
        {
            private readonly IEnumerator<Tuple<TKey, TValue>> _enumerator;

            public Enumerator(HandleableCollection<Tuple<TKey, TValue>> collection)
            {
                _enumerator = ((IEnumerable<Tuple<TKey, TValue>>)collection).GetEnumerator();
            }

            public void Dispose()
            {
                _enumerator.Dispose();
            }

            public bool MoveNext()
            {
                return _enumerator.MoveNext();
            }

            public void Reset()
            {
                _enumerator.Reset();
            }

            private TValue GetCurrentValue()
            {
                Tuple<TKey, TValue> tuple = _enumerator.Current;
                if (null == tuple)
                {
                    return default;
                }
                return tuple.Item2;
            }

            public TValue Current => GetCurrentValue();

            object IEnumerator.Current => GetCurrentValue();
        }
    }
}
