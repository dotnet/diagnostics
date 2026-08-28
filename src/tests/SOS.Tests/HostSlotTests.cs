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
        Assert.Same(HostSlot.Lldb, DumpSession.HostSlotFor(Host.Lldb));
        Assert.Same(HostSlot.DotNetDump, DumpSession.HostSlotFor(Host.DotnetDump));
        Assert.Null(DumpSession.HostSlotFor(Host.Cdb));
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

    private sealed class FakePooledHost : IPooledHost
    {
        private FakeDebuggerHost? _host;

        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public IDebuggerHost Host => _host ?? throw new InvalidOperationException("The host is not open.");

        public void OpenHost()
        {
            OpenCount++;
            _host = new FakeDebuggerHost();
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
