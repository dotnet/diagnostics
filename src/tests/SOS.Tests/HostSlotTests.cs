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
        Assert.Equal(2, HostSlot.CdbDump.Capacity);
        Assert.Equal(2, HostSlot.LldbDump.Capacity);
        Assert.Equal(1, HostSlot.DotNetDump.Capacity);
        Assert.NotNull(DumpSession.HostSlotFor(Host.Cdb));
        Assert.NotNull(DumpSession.HostSlotFor(Host.Lldb));
        HostSlot dotNetDumpFirst = DumpSession.HostSlotFor(Host.DotnetDump)!;
        HostSlot dotNetDumpSecond = DumpSession.HostSlotFor(Host.DotnetDump)!;
        Assert.Same(dotNetDumpFirst, dotNetDumpSecond);
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

        slot.Run(first, host => host.Sos("first"));
        slot.Run(first, host => host.Sos("again"));

        Assert.Equal(1, first.OpenCount);
        Assert.Equal(0, first.CloseCount);

        slot.Run(second, host => host.Sos("second"));

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

        slot.Run(first, host => host.Sos("first"));

        Assert.Throws<InvalidOperationException>(
            () => slot.Run(failing, host => host.Sos("unreachable")));
        Assert.Equal(1, first.CloseCount);
        Assert.Equal(1, failing.CloseCount);

        slot.Run(first, host => host.Sos("reopened"));

        Assert.Equal(2, first.OpenCount);
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
