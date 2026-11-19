// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class HostSlotTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EverySessionKindCanUseTheEntireBudget(int kind)
    {
        HostSlotPool pool = new(capacity: 2);
        CancellationToken token = TestContext.Current.CancellationToken;
        using HostSlotPool.Reservation first = await pool.ReserveAsync((SessionKind)kind, cancellationToken: token);
        using HostSlotPool.Reservation second = await pool.ReserveAsync((SessionKind)kind, cancellationToken: token);
        Task<HostSlotPool.Reservation> waiting = pool.ReserveAsync(SessionKind.Capture, cancellationToken: token);
        Assert.NotSame(first.Slot, second.Slot);
        Assert.False(waiting.IsCompleted);

        first.Dispose();
        using HostSlotPool.Reservation next = await waiting.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Same(first.Slot, next.Slot);
        Assert.Equal(2, pool.GetStatistics().PeakReserved);
    }

    [Fact]
    public void SwitchingOwnersEvictsTheOpenHost()
    {
        HostSlot slot = new();
        FakePooledHost first = new();
        FakePooledHost second = new();

        using (slot.Acquire(first))
        {
            first.Host.Sos("first");
        }
        using (slot.Acquire(first))
        {
            first.Host.Sos("again");
        }

        Assert.Equal(1, first.OpenCount);
        Assert.Equal(0, first.CloseCount);

        using (slot.Acquire(second))
        {
            second.Host.Sos("second");
        }

        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, second.OpenCount);

        slot.CloseCurrent();

        Assert.Equal(1, second.CloseCount);
    }

    [Fact]
    public void FailedReplacementDoesNotPoisonTheSlot()
    {
        HostSlot slot = new();
        FakePooledHost first = new();
        FakePooledHost failing = new() { ThrowOnOpen = true };

        using (slot.Acquire(first))
        {
            first.Host.Sos("first");
        }

        Assert.Throws<InvalidOperationException>(
            () => {
                using (slot.Acquire(failing))
                {
                    failing.Host.Sos("unreachable");
                }
            });
        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, failing.CloseCount);

        using (slot.Acquire(first))
        {
            first.Host.Sos("reopened");
        }

        Assert.Equal(2, first.OpenCount);
    }

    [Fact]
    public void LeaseKeepsTheOwnerOpenAcrossCommands()
    {
        HostSlot slot = new();
        FakePooledHost owner = new();
        FakePooledHost next = new();

        using (slot.Acquire(owner))
        {
            using (slot.Acquire(owner))
            {
                owner.Host.Sos("first");
            }
            using (slot.Acquire(owner))
            {
                owner.Host.Sos("second");
            }

            Assert.Equal(1, owner.OpenCount);
            Assert.Equal(0, owner.CloseCount);
        }

        using (slot.Acquire(next))
        {
            next.Host.Sos("next");
        }

        Assert.Equal(1, owner.CloseCount);
        Assert.Equal(1, next.OpenCount);
    }

    [Fact]
    public async Task OrdinarySessionCanMoveToAnyAvailableSlot()
    {
        HostSlotPool pool = new(capacity: 2);
        CancellationToken token = TestContext.Current.CancellationToken;
        FakePooledHost owner = new();
        using HostSlotPool.Reservation first = await pool.ReserveAsync(owner: owner, cancellationToken: token);
        using (first.Slot.Acquire(owner))
        {
        }
        first.Dispose();
        using HostSlotPool.Reservation other = await pool.ReserveAsync(cancellationToken: token);
        using HostSlotPool.Reservation capture = await pool.ReserveAsync(SessionKind.Capture, cancellationToken: token);
        Assert.Same(first.Slot, capture.Slot);
        Assert.Equal(1, owner.CloseCount);

        other.Dispose();
        using HostSlotPool.Reservation moved = await pool.ReserveAsync(owner: owner, cancellationToken: token);
        Assert.Same(other.Slot, moved.Slot);
        using (moved.Slot.Acquire(owner))
        {
            Assert.Equal(2, owner.OpenCount);
        }
        moved.Slot.CloseCurrent();
    }

    [Fact]
    public async Task CachedOwnerIsPreferredAndLeastRecentlyUsedHostIsEvicted()
    {
        HostSlotPool pool = new(capacity: 2);
        CancellationToken token = TestContext.Current.CancellationToken;
        FakePooledHost first = new();
        FakePooledHost second = new();
        Warm(first);
        Warm(second);
        Warm(first);

        using HostSlotPool.Reservation capture = await pool.ReserveAsync(SessionKind.Capture, cancellationToken: token);
        Assert.Equal(0, first.CloseCount);
        Assert.Equal(1, second.CloseCount);
        Assert.Equal(1, first.OpenCount);
        Assert.Equal(1, pool.GetStatistics().CacheHits);
        Assert.Equal(1, pool.GetStatistics().Evictions);
        pool.CloseAll();

        void Warm(FakePooledHost owner)
        {
            using HostSlotPool.Reservation reservation = pool.Reserve(owner: owner, cancellationToken: token);
            using (reservation.Slot.Acquire(owner))
            {
            }
        }
    }

    [Fact]
    public async Task CaptureEvictsIdleHostsButCannotEvictPinnedCollections()
    {
        HostSlotPool pool = new(capacity: 2);
        CancellationToken token = TestContext.Current.CancellationToken;
        FakePooledHost pinned = new();
        FakePooledHost idle = new();
        using HostSlotPool.Reservation collection = await pool.ReserveAsync(cancellationToken: token);
        using (collection.Slot.Acquire(pinned))
        {
        }
        using (HostSlotPool.Reservation ordinary = await pool.ReserveAsync(cancellationToken: token))
        {
            using (ordinary.Slot.Acquire(idle))
            {
            }
        }

        using HostSlotPool.Reservation capture = await pool.ReserveAsync(SessionKind.Capture, cancellationToken: token);
        Assert.Equal(1, idle.CloseCount);
        Assert.Equal(0, pinned.CloseCount);
        Task<HostSlotPool.Reservation> live = pool.ReserveAsync(SessionKind.Live, cancellationToken: token);
        Assert.False(live.IsCompleted);
        capture.Dispose();
        using HostSlotPool.Reservation next = await live.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.NotSame(collection.Slot, next.Slot);
        Assert.Equal(0, pinned.CloseCount);
        collection.Slot.CloseCurrent();
    }

    [Fact]
    public async Task FailedEvictionReturnsCapacity()
    {
        HostSlotPool pool = new(capacity: 1);
        CancellationToken token = TestContext.Current.CancellationToken;
        FakePooledHost owner = new() { OnClose = () => throw new InvalidOperationException("Close failed.") };
        using (HostSlotPool.Reservation reservation = await pool.ReserveAsync(cancellationToken: token))
        {
            using (reservation.Slot.Acquire(owner))
            {
            }
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pool.ReserveAsync(SessionKind.Live, cancellationToken: token));
        using HostSlotPool.Reservation next = await pool.ReserveAsync(SessionKind.Capture, cancellationToken: token)
            .WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Null(next.Slot.Owner);
        Assert.Equal(1, owner.CloseCount);
    }

    [Fact]
    public async Task OldHostTeardownCannotDisposeAReplacementInAnotherSlot()
    {
        HostSlotPool pool = new(capacity: 2);
        CancellationToken token = TestContext.Current.CancellationToken;
        TaskCompletionSource closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakePooledHost owner = new()
        {
            OnClose = () => {
                closing.TrySetResult();
                finishClose.Task.Wait(token);
            },
        };
        using (HostSlotPool.Reservation first = await pool.ReserveAsync(cancellationToken: token))
        {
            using (first.Slot.Acquire(owner))
            {
            }
        }
        using (HostSlotPool.Reservation second = await pool.ReserveAsync(cancellationToken: token))
        {
            using (second.Slot.Acquire(new FakePooledHost()))
            {
            }
        }

        Task<HostSlotPool.Reservation> capture = Task.Run(
            () => pool.Reserve(SessionKind.Capture, cancellationToken: token), token);
        try
        {
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            using HostSlotPool.Reservation moved = await pool.ReserveAsync(owner: owner, cancellationToken: token);
            FakeDebuggerHost replacement;
            using (moved.Slot.Acquire(owner))
            {
                replacement = Assert.IsType<FakeDebuggerHost>(moved.Slot.Host);
            }
            finishClose.SetResult();
            using HostSlotPool.Reservation captured = await capture.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(replacement.IsDisposed);
            Assert.Same(replacement, moved.Slot.Host);
        }
        finally
        {
            finishClose.TrySetResult();
            pool.CloseAll();
        }
    }

    [Fact]
    public async Task CancelledReservationDoesNotLoseCapacity()
    {
        HostSlotPool pool = new(capacity: 1);
        using HostSlotPool.Reservation held = await pool.ReserveAsync(cancellationToken: TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = new();
        Task<HostSlotPool.Reservation> waiting = pool.ReserveAsync(cancellationToken: cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        held.Dispose();

        using HostSlotPool.Reservation next = await pool.ReserveAsync(cancellationToken: TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Same(held.Slot, next.Slot);
    }

    [Fact]
    public async Task CancellationDuringEvictionReturnsCapacity()
    {
        HostSlotPool pool = new(capacity: 1);
        CancellationToken token = TestContext.Current.CancellationToken;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        FakePooledHost owner = new() { OnClose = cancellation.Cancel };
        using (HostSlotPool.Reservation reservation = await pool.ReserveAsync(cancellationToken: token))
        {
            using (reservation.Slot.Acquire(owner))
            {
            }
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pool.ReserveAsync(SessionKind.Live, cancellationToken: cancellation.Token));
        using HostSlotPool.Reservation next = await pool.ReserveAsync(cancellationToken: token)
            .WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Null(next.Slot.Owner);
        Assert.Equal(1, owner.CloseCount);
    }

    [Fact]
    public async Task InvalidRequestsCannotBypassEvictionOrLoseCapacity()
    {
        HostSlotPool pool = new(capacity: 1);
        CancellationToken token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => pool.ReserveAsync((SessionKind)(-1), cancellationToken: token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => pool.ReserveAsync(SessionKind.Live, owner: new FakePooledHost(), cancellationToken: token));
        using HostSlotPool.Reservation next = await pool.ReserveAsync(cancellationToken: token)
            .WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    [Fact]
    public async Task ReservationSurvivesThreadChangesAndKeepsOrdinaryCommandsOut()
    {
        HostSlotPool pool = new(capacity: 1);
        using HostSlotPool.Reservation collection = await pool.ReserveAsync(cancellationToken: TestContext.Current.CancellationToken);
        FakePooledHost owner = new();
        using (collection.Slot.Acquire(owner))
        {
            owner.Host.Sos("first");
        }

        Task<HostSlotPool.Reservation> ordinary = Task.Run(
            () => pool.Reserve(cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        using (collection.Slot.Acquire(owner))
        {
            owner.Host.Sos("second");
        }
        Assert.Equal(1, owner.OpenCount);
        Assert.Equal(0, owner.CloseCount);

        await Task.Run(collection.Dispose, TestContext.Current.CancellationToken);
        using HostSlotPool.Reservation next = await ordinary.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        using (next.Slot.Acquire(new FakePooledHost()))
        {
            Assert.Equal(1, owner.CloseCount);
        }
        next.Slot.CloseCurrent();
    }

    private sealed class FakePooledHost : IPooledHost
    {
        private FakeDebuggerHost? _host;

        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public bool ThrowOnOpen { get; init; }
        public Action? OnClose { get; init; }
        public IDebuggerHost Host => _host ?? throw new InvalidOperationException("The host is not open.");

        public IDebuggerHost CreateHost()
        {
            OpenCount++;
            _host = new FakeDebuggerHost(() => {
                CloseCount++;
                OnClose?.Invoke();
            }, ThrowOnOpen);
            return _host;
        }
    }

    private sealed class FakeDebuggerHost(Action onDispose, bool throwOnLoad) : IDebuggerHost
    {
        public string Name => "fake";
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            onDispose();
        }

        public void LoadSos()
        {
            if (throwOnLoad)
            {
                throw new InvalidOperationException("Open failed.");
            }
        }

        public SosOutput Execute(string command) => new(Name, command, string.Empty);

        public SosOutput Sos(string command) => new(Name, command, string.Empty);
    }
}
