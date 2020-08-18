// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class KeyedHandleableCollectionTests
    {
        private static readonly TimeSpan DefaultHandleTimeout = TimeSpan.FromMilliseconds(50);

        private readonly ITestOutputHelper _outputHelper;

        public KeyedHandleableCollectionTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public async Task KeyedHandleableCollectionThrowsWhenDisposedTest()
        {
            var collection = new KeyedHandleableCollection<int, string>();

            AddRangeAndVerifyItems(collection, endInclusive: 9);

            KeyedHandleableCollection<int, string>.KeyedHandler handler = (in string item, out bool removeItem) =>
            {
                removeItem = false;
                return "none" == item;
            };

            TimeSpan longTimeout = TimeSpan.FromSeconds(5);
            using var cancellation = new CancellationTokenSource(longTimeout);

            Task handleTask = Task.Run(() => collection.Handle(20, handler, longTimeout));
            Task handleAsyncTask = collection.HandleAsync(25, handler, cancellation.Token);

            Task delayTask = Task.Delay(DefaultHandleTimeout);
            Task completedTask = await Task.WhenAny(delayTask, handleTask, handleAsyncTask);

            // Check that the handle tasks didn't complete
            Assert.Equal(delayTask, completedTask);

            collection.Dispose();

            // Incomplete calls from prior to disposal should throw ObjectDisposedException
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handleTask);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handleAsyncTask);

            // New calls should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(
                () => collection.Add(10, "ten"));

            Assert.Throws<ObjectDisposedException>(
                () => collection.Handle(10, DefaultHandleTimeout));

            Assert.Throws<ObjectDisposedException>(
                () => collection.Handle(10, handler, DefaultHandleTimeout));

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => collection.HandleAsync(10, cancellation.Token));

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => collection.HandleAsync(10, handler, cancellation.Token));

            HandleableCollection<int>.Predicate predicate = (in int item) => false;

            Assert.Throws<ObjectDisposedException>(
                () => collection.TryRemove(10, out string oldItem));

            Assert.Throws<ObjectDisposedException>(
                () => collection.TryReplace(10, "ten", out string oldItem));

            Assert.Throws<ObjectDisposedException>(
                () => ((IEnumerable)collection).GetEnumerator());

            Assert.Throws<ObjectDisposedException>(
                () => ((IEnumerable<Tuple<int, string>>)collection).GetEnumerator());

            Assert.Throws<ObjectDisposedException>(
                () => collection.Values);
        }

        [Fact]
        public async Task KeyedHandleableCollectionDefaultHandlerTest()
        {
            await KeyedHandleableCollectionDefaultHandlerTestCore(useAsync: false);
        }

        [Fact]
        public async Task KeyedHandleableCollectionDefaultHandlerTestAsync()
        {
            await KeyedHandleableCollectionDefaultHandlerTestCore(useAsync: true);
        }

        /// <summary>
        /// Tests that the default handler handles one item at a time and
        /// removes each item after each successful handling.
        /// </summary>
        private async Task KeyedHandleableCollectionDefaultHandlerTestCore(bool useAsync)
        {
            using var collection = new KeyedHandleableCollection<int, string>();
            Assert.Empty(collection);

            var shim = new KeyedHandleableCollectionApiShim<int, string>(collection, useAsync);

            AddRangeAndVerifyItems(collection, endInclusive: 9);

            string[] expectedValues = collection.Values.ToArray();

            int expectedCollectionCount = collection.Count();
            for (int key = 0; key < 10; key++)
            {
                string handledValue = await shim.Handle(key, DefaultHandleTimeout);
                expectedCollectionCount--;

                Assert.Equal(expectedValues[key], handledValue);
                Assert.Equal(expectedCollectionCount, collection.Count());
            }

            Assert.Empty(collection);

            await shim.HandleThrowsForTimeout(20, DefaultHandleTimeout);
        }

        [Fact]
        public async Task KeyedHandleableCollectionComplexHandlerTest()
        {
            await KeyedHandleableCollectionComplexHandlerTestCore(useAsync: false);
        }

        [Fact]
        public async Task KeyedHandleableCollectionComplexHandlerTestAsync()
        {
            await KeyedHandleableCollectionComplexHandlerTestCore(useAsync: true);
        }

        /// <summary>
        /// Tests that a non-default handler can remove items independently from
        /// handling an item and that the handled item is the item on which
        /// the handler completed.
        /// </summary>
        private async Task KeyedHandleableCollectionComplexHandlerTestCore(bool useAsync)
        {
            using var collection = new KeyedHandleableCollection<int, string>();
            Assert.Empty(collection);

            var shim = new KeyedHandleableCollectionApiShim<int, string>(collection, useAsync);

            AddRangeAndVerifyItems(collection, endInclusive: 4);

            KeyedHandleableCollection<int, string>.KeyedHandler handler = (in string item, out bool removeItem) =>
            {
                // Remove any values whose length is 3
                if (item.Length == 3)
                {
                    removeItem = true;
                    return false;
                }

                removeItem = false;
                return true;
            };

            Task<string> handledValueTask = Task.Run(() => shim.Handle(2, handler, 5 * DefaultHandleTimeout));

            Task delayTask = Task.Delay(DefaultHandleTimeout);
            Task completedTask = await Task.WhenAny(delayTask, handledValueTask);

            // Check that the handler task didn't complete
            Assert.Equal(delayTask, completedTask);

            // Handler should have removed (2, "two") from the collection
            Assert.Equal(4, collection.Count());
            Assert.Equal(new string[] { "zero", "one", "three", "four" }, collection.Values);

            // Handler should complete on this pair
            const string expectedValue = "one + one";
            collection.Add(2, expectedValue);

            delayTask = Task.Delay(DefaultHandleTimeout);
            completedTask = await Task.WhenAny(delayTask, handledValueTask);

            // Check that the handler did complete
            Assert.Equal(handledValueTask, completedTask);

            // Check that it got the correct value
            string handledValue = await handledValueTask;
            Assert.Equal(expectedValue, handledValue);

            // Check that handler did not remove the value
            Assert.Equal(5, collection.Count());
            Assert.Equal(new string[] { "zero", "one", "three", "four", expectedValue }, collection.Values);
        }

        [Fact]
        public async Task KeyedHandleableCollectionHandleBeforeAddTest()
        {
            await KeyedHandleableCollectionHandleBeforeAddTestCore(useAsync: false);
        }

        [Fact]
        public async Task KeyedHandleableCollectionHandleBeforeAddTestAsync()
        {
            await KeyedHandleableCollectionHandleBeforeAddTestCore(useAsync: true);
        }

        /// <summary>
        /// Tests that handler can be added before an item is provided to the collection.
        /// </summary>
        private async Task KeyedHandleableCollectionHandleBeforeAddTestCore(bool useAsync)
        {
            using var collection = new TestKeyedHandleableCollection<int, string>();
            Assert.Empty(collection);

            var shim = new KeyedHandleableCollectionApiShim<int, string>(collection, useAsync);

            // Register to be notified when handler is beginning to be processed
            Task handlerBeginTask = collection.WaitForHandlerBeginAsync(DefaultHandleTimeout);

            const string expectedValue = "three";

            // Create task that will start handling BEFORE an item is added
            Task<string> handleValueTask = Task.Run(() => shim.Handle(3, DefaultHandleTimeout));

            // Wait for handler to begin processing
            await handlerBeginTask;

            IList<(int, string, int)> itemsAndCounts = new List<(int, string, int)>()
            {
                (0, "zero", 1),
                (1, "one", 2),
                (2, "two", 3),
                (3, "three", 3), // Item is consumed immediately, thus collection count does not change
                (4, "four", 4),
                (5, "five", 5)
            };
            AddAndVerifyItems(collection, itemsAndCounts);

            // Wait for handled item to be returned
            string handledValue = await handleValueTask;

            Assert.Equal(expectedValue, handledValue);
            Assert.Equal(new string[] { "zero", "one", "two", "four", "five" }, collection.Values);
        }

        /// <summary>
        /// Tests that the <see cref="KeyedHandleableCollection{TKey, TValue}.TryRemove(TKey, out TValue)"/> method
        /// removes a value if a corresponding key is found and does not make modifications if the key is not found.
        [Fact]
        public void KeyedHandleableCollectionTryRemoveTest()
        {
            using var collection = new KeyedHandleableCollection<int, string>();
            Assert.Empty(collection);

            AddRangeAndVerifyItems(collection, endInclusive: 4);

            string oldValue;
            Assert.True(collection.TryRemove(2, out oldValue));
            Assert.Equal("two", oldValue);
            Assert.Equal(new string[] { "zero", "one", "three", "four" }, collection.Values);

            Assert.False(collection.TryRemove(7, out oldValue));
            Assert.Equal(default, oldValue);
            Assert.Equal(new string[] { "zero", "one", "three", "four" }, collection.Values);
        }

        /// <summary>
        /// Tests that the <see cref="KeyedHandleableCollection{TKey, TValue}.TryReplace(TKey, TValue, out TValue)"/> method
        /// replaces a value if a corresponding key is found and does not make modifications if the key is not found.
        /// </summary>
        [Fact]
        public void KeyedHandleableCollectionTryReplaceTest()
        {
            using var collection = new KeyedHandleableCollection<int, string>();
            Assert.Empty(collection);

            AddRangeAndVerifyItems(collection, endInclusive: 4);

            string oldValue;
            Assert.True(collection.TryReplace(2, "one + one", out oldValue));
            Assert.Equal("two", oldValue);
            Assert.Equal(new string[] { "zero", "one", "one + one", "three", "four" }, collection.Values);

            Assert.False(collection.TryReplace(7, "three + four", out oldValue));
            Assert.Equal(default, oldValue);
            Assert.Equal(new string[] { "zero", "one", "one + one", "three", "four" }, collection.Values);
        }

        private static void AddAndVerifyItems<TKey, TValue>(KeyedHandleableCollection<TKey, TValue> collection, IEnumerable<(TKey, TValue, int)> itemsAndCounts)
        {
            // Pairs of (item to be added, expected collection count after adding item)
            foreach ((TKey key, TValue value, int count) in itemsAndCounts)
            {
                collection.Add(key, value);
                Assert.Equal(count, collection.Count());
            }
        }

        private static void AddRangeAndVerifyItems(KeyedHandleableCollection<int, string> collection, int endInclusive)
        {
            if (endInclusive < 0 || endInclusive > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(endInclusive));
            }

            IList<(int, string, int)> itemsAndCounts = new List<(int, string, int)>();
            if (endInclusive >= 0)
            {
                itemsAndCounts.Add((0, "zero", 1));
            }
            if (endInclusive >= 1)
            {
                itemsAndCounts.Add((1, "one", 2));
            }
            if (endInclusive >= 2)
            {
                itemsAndCounts.Add((2, "two", 3));
            }
            if (endInclusive >= 3)
            {
                itemsAndCounts.Add((3, "three", 4));
            }
            if (endInclusive >= 4)
            {
                itemsAndCounts.Add((4, "four", 5));
            }
            if (endInclusive >= 5)
            {
                itemsAndCounts.Add((5, "five", 6));
            }
            if (endInclusive >= 6)
            {
                itemsAndCounts.Add((6, "six", 7));
            }
            if (endInclusive >= 7)
            {
                itemsAndCounts.Add((7, "seven", 8));
            }
            if (endInclusive >= 8)
            {
                itemsAndCounts.Add((8, "eight", 9));
            }
            if (endInclusive >= 9)
            {
                itemsAndCounts.Add((9, "nine", 10));
            }
            AddAndVerifyItems(collection, itemsAndCounts);
        }

        private class KeyedHandleableCollectionApiShim<TKey, TValue>
        {
            private KeyedHandleableCollection<TKey, TValue> _collection;
            private readonly bool _useAsync;

            public KeyedHandleableCollectionApiShim(KeyedHandleableCollection<TKey, TValue> collection, bool useAsync)
            {
                _collection = collection;
                _useAsync = useAsync;
            }

            public async Task<TValue> Handle(TKey key, TimeSpan timeout)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    return await _collection.HandleAsync(key, cancellation.Token);
                }
                else
                {
                    return _collection.Handle(key, timeout);
                }
            }

            public async Task<TValue> Handle(TKey key, KeyedHandleableCollection<TKey, TValue>.KeyedHandler handler, TimeSpan timeout)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    return await _collection.HandleAsync(key, handler, cancellation.Token);
                }
                else
                {
                    return _collection.Handle(key, handler, timeout);
                }
            }

            public async Task HandleThrowsForTimeout(TKey key, TimeSpan timeout)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    await Assert.ThrowsAsync<TaskCanceledException>(() => _collection.HandleAsync(key, cancellation.Token));
                }
                else
                {
                    Assert.Throws<TimeoutException>(() => _collection.Handle(key, timeout));
                }
            }
        }

        private class TestKeyedHandleableCollection<TKey, TValue> : KeyedHandleableCollection<TKey, TValue>
        {
            private readonly List<TaskCompletionSource<object>> _handlerBeginSources = new List<TaskCompletionSource<object>>();

            protected override void OnHandlerBegin()
            {
                lock (_handlerBeginSources)
                {
                    foreach (var source in _handlerBeginSources)
                    {
                        source.TrySetResult(null);
                    }
                    _handlerBeginSources.Clear();
                }
            }

            public async Task WaitForHandlerBeginAsync(TimeSpan timeout)
            {
                TaskCompletionSource<object> handlerBeginSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var timeoutCancellation = new CancellationTokenSource();
                var token = timeoutCancellation.Token;
                using var _ = token.Register(() => handlerBeginSource.TrySetCanceled(token));

                lock (_handlerBeginSources)
                {
                    _handlerBeginSources.Add(handlerBeginSource);
                }

                timeoutCancellation.CancelAfter(timeout);
                await handlerBeginSource.Task;
            }
        }
    }
}
