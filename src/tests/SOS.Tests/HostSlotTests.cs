// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class HostSlotTests
{
    [Fact]
    public void DumpSessionsUseSeparateBoundedSlots()
    {
        AssertTwoSlotPool(new HostSlotPool(capacity: 2));
        Assert.Equal(2, HostSlotPool.Cdb.Capacity);
        Assert.Equal(2, HostSlotPool.Lldb.Capacity);
        Assert.Equal(2, HostSlotPool.DotNetDump.Capacity);
        Assert.NotNull(HostSlotPool.HostSlotFor(Host.Cdb));
        Assert.NotNull(HostSlotPool.HostSlotFor(Host.Lldb));
        HostSlot dotNetDumpFirst = HostSlotPool.HostSlotFor(Host.DotnetDump);
        HostSlot dotNetDumpSecond = HostSlotPool.HostSlotFor(Host.DotnetDump);
        HostSlot dotNetDumpThird = HostSlotPool.HostSlotFor(Host.DotnetDump);
        Assert.NotSame(dotNetDumpFirst, dotNetDumpSecond);
        Assert.Same(dotNetDumpFirst, dotNetDumpThird);
        Assert.Throws<ArgumentOutOfRangeException>(() => HostSlotPool.HostSlotFor(Host.AllValid));
    }

    private static void AssertTwoSlotPool(HostSlotPool pool)
    {
        HostSlot first = pool.Select();
        HostSlot second = pool.Select();
        HostSlot third = pool.Select();

        Assert.Equal(2, pool.Capacity);
        Assert.NotSame(first, second);
        Assert.Same(first, third);
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
            () =>
            {
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

    private sealed class FakePooledHost : IPooledHost
    {
        private FakeDebuggerHost? _host;

        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public bool ThrowOnOpen { get; init; }
        public IDebuggerHost Host => _host ?? throw new InvalidOperationException("The host is not open.");

        public void OpenHost()
        {
            OpenCount++;
            _host = new FakeDebuggerHost();
            if (ThrowOnOpen)
            {
                throw new InvalidOperationException("Open failed.");
            }
        }

        public void CloseHost()
        {
            CloseCount++;
            _host?.Dispose();
            _host = null;
        }
    }

    private sealed class FakeDebuggerHost : IDebuggerHost
    {
        public string Name => "fake";

        public void Dispose()
        {
        }

        public void LoadSos()
        {
        }

        public SosOutput Execute(string command) => new(Name, command, string.Empty);

        public SosOutput Sos(string command) => new(Name, command, string.Empty);
    }
}
