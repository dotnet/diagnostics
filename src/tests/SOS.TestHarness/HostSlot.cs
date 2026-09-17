// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// A host whose lifetime a <see cref="HostSlot"/> may open and close on demand.
/// </summary>
internal interface IPooledHost
{
    IDebuggerHost Host { get; }

    void OpenHost();

    void CloseHost();
}

/// <summary>
/// Distributes memoized dump sessions across a fixed number of independent <see cref="HostSlot"/>
/// instances. Each session remains assigned to one slot, preserving that slot's most-recently-used
/// host reuse while allowing sessions assigned to different slots to execute concurrently.
/// </summary>
internal sealed class HostSlotPool
{
    /// <summary>Two independent out-of-process cdb dump slots.</summary>
    public static readonly HostSlotPool Cdb = new(capacity: 2);

    /// <summary>Two independent dotnet-dump analyze slots.</summary>
    public static readonly HostSlotPool DotNetDump = new(capacity: 2);

    /// <summary>Two independent LLDB dump slots.</summary>
    public static readonly HostSlotPool Lldb = new(capacity: 2);

    private readonly HostSlot[] _slots;
    private int _next = -1;

    public int Capacity => _slots.Length;

    public HostSlotPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _slots = new HostSlot[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _slots[i] = new HostSlot();
        }
    }

    public static HostSlot HostSlotFor(Host hostKind) => hostKind switch
    {
        Host.Cdb => Cdb.Select(),
        Host.Lldb => Lldb.Select(),
        Host.DotnetDump => DotNetDump.Select(),
        _ => throw new ArgumentOutOfRangeException(nameof(hostKind), hostKind, "Unsupported dump host."),
    };

    /// <summary>Assign the next session to a slot, distributing sessions round-robin.</summary>
    public HostSlot Select()
    {
        int index = (int)((uint)Interlocked.Increment(ref _next) % (uint)_slots.Length);
        return _slots[index];
    }

    /// <summary>Close the currently-open host in every slot.</summary>
    public void CloseAll()
    {
        foreach (HostSlot slot in _slots)
        {
            slot.CloseCurrent();
        }
    }
}

/// <summary>
/// Governs one live host instance within an individual slot. Pools provide bounded concurrency.
///
/// <b>dotnet-dump</b>, <b>cdb</b>, and <b>lldb</b> retain loaded dump state, so small fixed pools
/// preserve limited concurrency without unbounded memory growth.
/// The most-recently-used host stays open and is evicted (disposed) only when a different target
/// of the same kind is needed — so a run of assertions against one dump reuses the open host, and
/// switching dumps reopens (cheap relative to the work).
/// </summary>
internal sealed class HostSlot
{
    private readonly object _lock = new();
    private IPooledHost? _open;

    /// <summary>
    /// Ensure <paramref name="owner"/>'s host is open and hold exclusive use of this slot until the
    /// returned lease is disposed. The lease must be disposed on the acquiring thread.
    /// </summary>
    public IDisposable Acquire(IPooledHost owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Monitor.Enter(_lock);
        try
        {
            if (!ReferenceEquals(_open, owner))
            {
                IPooledHost? previous = _open;
                _open = null;
                previous?.CloseHost();
                try
                {
                    owner.OpenHost();
                }
                catch (Exception openException)
                {
                    try
                    {
                        owner.CloseHost();
                    }
                    catch (Exception closeException)
                    {
                        throw new AggregateException(
                            "Opening the pooled host failed, and cleaning up the partial host also failed.",
                            openException,
                            closeException);
                    }

                    throw;
                }

                _open = owner;
            }

            return new Lease(_lock);
        }
        catch
        {
            Monitor.Exit(_lock);
            throw;
        }
    }

    /// <summary>Close the currently-open host, if any (teardown).</summary>
    public void CloseCurrent()
    {
        lock (_lock)
        {
            IPooledHost? open = _open;
            _open = null;
            open?.CloseHost();
        }
    }

    private sealed class Lease : IDisposable
    {
        private readonly object _gate;
        private bool _disposed;

        public Lease(object gate) => _gate = gate;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Monitor.Exit(_gate);
        }
    }

}
