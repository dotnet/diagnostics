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
    public class HandleableCollectionTests
    {
        // Generous timeout to allow APIs to respond on slower or more constrained machines
        private static readonly TimeSpan DefaultPositiveVerificationTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultNegativeVerificationTimeout = TimeSpan.FromSeconds(2);

        private readonly ITestOutputHelper _outputHelper;

        public HandleableCollectionTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public async Task HandleableCollectionThrowsWhenDisposedTest()
        {
            var collection = new HandleableCollection<int>();

            AddRangeAndVerifyItems(collection, endInclusive: 9);

            HandleableCollection<int>.Handler handler = (in int item, out bool removeItem) =>
            {
                removeItem = false;
                return 20 == item;
            };

            using var cancellation = new CancellationTokenSource(DefaultPositiveVerificationTimeout);

            Task handleTask = Task.Run(() => collection.Handle(handler, DefaultPositiveVerificationTimeout));
            Task handleAsyncTask = collection.HandleAsync(handler, cancellation.Token);

            // Task.Delay intentionally shorter than default timeout to check that Handle*
            // calls did not complete quickly.
            Task delayTask = Task.Delay(TimeSpan.FromSeconds(1));
            Task completedTask = await Task.WhenAny(delayTask, handleTask, handleAsyncTask);

            // Check that the handle tasks didn't complete
            Assert.Equal(delayTask, completedTask);

            collection.Dispose();

            // Incomplete calls from prior to disposal should throw ObjectDisposedException
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handleTask);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => handleAsyncTask);

            // New calls should throw ObjectDisposedException
            Assert.Throws<ObjectDisposedException>(
                () => collection.Add(10));

            Assert.Throws<ObjectDisposedException>(
                () => collection.Handle(DefaultPositiveVerificationTimeout));

            Assert.Throws<ObjectDisposedException>(
                () => collection.Handle(handler, DefaultPositiveVerificationTimeout));

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => collection.HandleAsync(cancellation.Token));

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => collection.HandleAsync(handler, cancellation.Token));

            HandleableCollection<int>.Predicate predicate = (in int item) => false;

            Assert.Throws<ObjectDisposedException>(
                () => collection.TryRemove(predicate, out int oldItem));

            Assert.Throws<ObjectDisposedException>(
                () => collection.TryReplace(predicate, 10, out int oldItem));

            Assert.Throws<ObjectDisposedException>(
                () => ((IEnumerable)collection).GetEnumerator());

            Assert.Throws<ObjectDisposedException>(
                () => ((IEnumerable<int>)collection).GetEnumerator());
        }

        [Fact]
        public async Task HandleableCollectionDefaultHandlerTest()
        {
            await HandleableCollectionDefaultHandlerTestCore(useAsync: false);
        }

        [Fact]
        public async Task HandleableCollectionDefaultHandlerTestAsync()
        {
            await HandleableCollectionDefaultHandlerTestCore(useAsync: true);
        }

        /// <summary>
        /// Tests that the default handler handles one item at a time and
        /// removes each item after each successful handling.
        /// </summary>
        private async Task HandleableCollectionDefaultHandlerTestCore(bool useAsync)
        {
            using var collection = new HandleableCollection<int>();
            Assert.Empty(collection);

            var shim = new HandleableCollectionApiShim<int>(collection, useAsync);

            AddRangeAndVerifyItems(collection, endInclusive: 14);

            int expectedCollectionCount = collection.Count();
            for (int item = 0; item < 15; item++)
            {
                int handledItem = await shim.Handle(DefaultPositiveVerificationTimeout);
                expectedCollectionCount--;

                Assert.Equal(item, handledItem);
                Assert.Equal(expectedCollectionCount, collection.Count());
            }

            Assert.Empty(collection);

            await shim.HandleThrowsForTimeout(DefaultNegativeVerificationTimeout);
        }

        [Fact]
        public async Task HandleableCollectionComplexHandlerTest()
        {
            await HandleableCollectionComplexHandlerTestCore(useAsync: false);
        }

        [Fact]
        public async Task HandleableCollectionComplexHandlerTestAsync()
        {
            await HandleableCollectionComplexHandlerTestCore(useAsync: true);
        }

        /// <summary>
        /// Tests that a non-default handler can remove items independently from
        /// handling an item and that the handled item is the item on which
        /// the handler completed.
        /// </summary>
        private async Task HandleableCollectionComplexHandlerTestCore(bool useAsync)
        {
            using var collection = new HandleableCollection<int>();
            Assert.Empty(collection);

            var shim = new HandleableCollectionApiShim<int>(collection, useAsync);

            const int expectedItem = 7;
            AddRangeAndVerifyItems(collection, endInclusive: expectedItem);

            HandleableCollection<int>.Handler handler = (in int item, out bool removeItem) =>
            {
                removeItem = false;

                // Remove every third item
                if (item % 3 == 0)
                {
                    removeItem = true;
                }

                // Terminate handler on last item
                return expectedItem == item;
            };

            int handledItem = await shim.Handle(handler, DefaultPositiveVerificationTimeout);
            Assert.Equal(expectedItem, handledItem);
            Assert.Equal(new int[] { 1, 2, 4, 5, 7 }, collection);
        }

        [Fact]
        public async Task HandleableCollectionHandleBeforeAddTest()
        {
            await HandleableCollectionHandleBeforeAddTestCore(useAsync: false);
        }

        [Fact]
        public async Task HandleableCollectionHandleBeforeAddTestAsync()
        {
            await HandleableCollectionHandleBeforeAddTestCore(useAsync: true);
        }

        /// <summary>
        /// Tests that handler can be added before an item is provided to the collection.
        /// </summary>
        private async Task HandleableCollectionHandleBeforeAddTestCore(bool useAsync)
        {
            using var collection = new TestHandleableCollection<int>();
            Assert.Empty(collection);

            var shim = new HandleableCollectionApiShim<int>(collection, useAsync);

            // Register to be notified when handler is beginning to be processed
            Task handlerBeginTask = collection.WaitForHandlerBeginAsync(DefaultPositiveVerificationTimeout);

            const int expectedItem = 3;
            HandleableCollection<int>.Handler handler = (in int item, out bool removeItem) =>
            {
                // Terminate handler on some item in the middle of the collection
                if (expectedItem == item)
                {
                    removeItem = true;
                    return true;
                }

                removeItem = false;
                return false;
            };

            // Create task that will start handling BEFORE an item is added
            Task<int> handleItemTask = Task.Run(() => shim.Handle(handler, DefaultPositiveVerificationTimeout));

            // Wait for handler to begin processing
            Task delayTask = Task.Delay(5 * DefaultPositiveVerificationTimeout);
            Task completedTask = await Task.WhenAny(delayTask, handlerBeginTask);
            Assert.Equal(handlerBeginTask, completedTask);

            IList<(int, int)> itemsAndCounts = new List<(int, int)>()
            {
                (0, 1),
                (1, 2),
                (2, 3),
                (3, 3), // Item is consumed immediately, thus collection count does not change
                (4, 4),
                (5, 5)
            };
            AddAndVerifyItems(collection, itemsAndCounts);

            // Wait for handled item to be returned
            int handledItem = await handleItemTask;

            Assert.Equal(expectedItem, handledItem);
            Assert.Equal(new int[] { 0, 1, 2, 4, 5 }, collection);
        }

        [Fact]
        public async Task HandleableCollectionHandleNoRemovalTest()
        {
            await HandleableCollectionHandleNoRemovalTestCore(useAsync: false);
        }

        [Fact]
        public async Task HandleableCollectionHandleNoRemovalTestAsync()
        {
            await HandleableCollectionHandleNoRemovalTestCore(useAsync: true);
        }

        /// <summary>
        /// Tests that handler does not have to remove an item from the collection
        /// and that the handled item is the item on which the handler completed.
        /// </summary>
        private async Task HandleableCollectionHandleNoRemovalTestCore(bool useAsync)
        {
            using var collection = new HandleableCollection<int>();
            Assert.Empty(collection);

            var shim = new HandleableCollectionApiShim<int>(collection, useAsync);

            AddRangeAndVerifyItems(collection, endInclusive: 4);

            const int expectedItem = 2;
            HandleableCollection<int>.Handler handler = (in int item, out bool removeItem) =>
            {
                // Do not remove any item (the purpose of this test is to handle an
                // item without removing it).
                removeItem = false;

                // Terminate handler on some item in the middle of the collection
                return expectedItem == item;
            };

            // Wait for handled item to be returned
            int handledItem = await shim.Handle(handler, DefaultPositiveVerificationTimeout);

            Assert.Equal(expectedItem, handledItem);
            Assert.Equal(new int[] { 0, 1, 2, 3, 4 }, collection);
        }

        /// <summary>
        /// Tests that the <see cref="HandleableCollection{T}.TryRemove(HandleableCollection{T}.Predicate, out T)"/> method
        /// removes an item if the predicate is satisfied and does not make modifications if not satisfied.
        /// </summary>
        [Fact]
        public void HandleableCollectionTryRemoveTest()
        {
            using var collection = new HandleableCollection<int>();
            Assert.Empty(collection);

            AddRangeAndVerifyItems(collection, endInclusive: 4);

            const int expectedItem = 2;
            HandleableCollection<int>.Predicate successPredicate = (in int item) =>
            {
                return expectedItem == item;
            };

            int oldItem;
            Assert.True(collection.TryRemove(successPredicate, out oldItem));
            Assert.Equal(expectedItem, oldItem);
            Assert.Equal(new int[] { 0, 1, 3, 4 }, collection);

            HandleableCollection<int>.Predicate failedPredicate = (in int item) =>
            {
                return 8 == item;
            };

            Assert.False(collection.TryRemove(failedPredicate, out oldItem));
            Assert.Equal(default, oldItem);
            Assert.Equal(new int[] { 0, 1, 3, 4 }, collection);
        }

        /// <summary>
        /// Tests that the <see cref="HandleableCollection{T}.TryReplace(HandleableCollection{T}.Predicate, in T, out T)"/> method
        /// replaces an item if the predicate is satisfied and does not make modifications if not satisfied.
        /// </summary>
        [Fact]
        public void HandleableCollectionTryReplaceTest()
        {
            using var collection = new HandleableCollection<int>();
            Assert.Empty(collection);

            AddRangeAndVerifyItems(collection, endInclusive: 4);

            const int expectedItem = 2;
            HandleableCollection<int>.Predicate successPredicate = (in int item) =>
            {
                return expectedItem == item;
            };

            int oldItem;
            Assert.True(collection.TryReplace(successPredicate, 10, out oldItem));
            Assert.Equal(expectedItem, oldItem);
            Assert.Equal(new int[] { 0, 1, 10, 3, 4 }, collection);

            HandleableCollection<int>.Predicate failedPredicate = (in int item) =>
            {
                return 8 == item;
            };

            Assert.False(collection.TryReplace(failedPredicate, 15, out oldItem));
            Assert.Equal(default, oldItem);
            Assert.Equal(new int[] { 0, 1, 10, 3, 4 }, collection);
        }

        private static void AddAndVerifyItems<T>(HandleableCollection<T> collection, IEnumerable<(T, int)> itemsAndCounts)
        {
            // Pairs of (item to be added, expected collection count after adding item)
            foreach ((T item, int count) in itemsAndCounts)
            {
                collection.Add(item);
                Assert.Equal(count, collection.Count());
            }
        }

        private static void AddRangeAndVerifyItems(HandleableCollection<int> collection, int endInclusive)
        {
            if (endInclusive < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(endInclusive));
            }

            IList<(int, int)> itemsAndCounts = new List<(int, int)>();
            for (int item = 0; item <= endInclusive; item++)
            {
                itemsAndCounts.Add((item, item + 1));
            }
            AddAndVerifyItems(collection, itemsAndCounts);
        }

        private class HandleableCollectionApiShim<T>
        {
            private HandleableCollection<T> _collection;
            private readonly bool _useAsync;

            public HandleableCollectionApiShim(HandleableCollection<T> collection, bool useAsync)
            {
                _collection = collection;
                _useAsync = useAsync;
            }

            public async Task<T> Handle(TimeSpan timeout)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    return await _collection.HandleAsync(cancellation.Token);
                }
                else
                {
                    return _collection.Handle(timeout);
                }
            }

            public async Task<T> Handle(HandleableCollection<T>.Handler handler, TimeSpan timeout)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    return await _collection.HandleAsync(handler, cancellation.Token);
                }
                else
                {
                    return _collection.Handle(handler, timeout);
                }
            }

            public async Task HandleThrowsForTimeout(TimeSpan timeout)
            {
                if (_useAsync)
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    await Assert.ThrowsAsync<TaskCanceledException>(() => _collection.HandleAsync(cancellation.Token));
                }
                else
                {
                    Assert.Throws<TimeoutException>(() => _collection.Handle(timeout));
                }
            }
        }

        private class TestHandleableCollection<T> : HandleableCollection<T>
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
