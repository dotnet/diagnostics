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
/// Debugger operations need this for different reasons:
/// <list type="bullet">
///   <item><b>DbgEng capture</b> runs in-process and is genuinely one-instance-per-process.</item>
///   <item><b>dotnet-dump</b>, <b>cdb</b>, and <b>lldb</b> retain loaded dump state, so small fixed
///   pools preserve limited concurrency without unbounded memory growth.</item>
/// </list>
/// The most-recently-used host stays open and is evicted (disposed) only when a different target
/// of the same kind is needed — so a run of assertions against one dump reuses the open host, and
/// switching dumps reopens (cheap relative to the work). Live targets take an exclusive lease for
/// their lifetime.
/// </summary>
internal sealed class HostSlot
{
    /// <summary>The in-process dbgeng dump-capture slot.</summary>
    public static readonly HostSlot DbgEngCapture = new();

    /// <summary>Two independent out-of-process cdb dump slots.</summary>
    public static readonly HostSlotPool CdbDump = new(capacity: 2);

    /// <summary>Two independent dotnet-dump analyze slots.</summary>
    public static readonly HostSlotPool DotNetDump = new(capacity: 2);

    /// <summary>Two independent LLDB dump slots.</summary>
    public static readonly HostSlotPool LldbDump = new(capacity: 2);

    private readonly object _lock = new();
    private IPooledHost? _open;
    private bool _exclusiveHeld;

    /// <summary>
    /// Ensure <paramref name="owner"/>'s host is the one open host for this slot, then run
    /// <paramref name="action"/> against it. Serializes all work on this slot.
    /// </summary>
    public SosOutput Run(IPooledHost owner, Func<IDebuggerHost, SosOutput> action)
    {
        lock (_lock)
        {
            while (_exclusiveHeld)
            {
                System.Threading.Monitor.Wait(_lock);
            }

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

            return action(owner.Host);
        }
    }

    /// <summary>
    /// Acquire exclusive use of this slot for a live target's lifetime. Evicts any open host and
    /// blocks other use until the returned lease is disposed.
    /// </summary>
    public IDisposable AcquireExclusive()
    {
        lock (_lock)
        {
            while (_exclusiveHeld)
            {
                System.Threading.Monitor.Wait(_lock);
            }

            IPooledHost? open = _open;
            _open = null;
            open?.CloseHost();
            _exclusiveHeld = true;
        }

        return new Lease(this);
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
        private readonly HostSlot _slot;
        private bool _disposed;

        public Lease(HostSlot slot) => _slot = slot;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_slot._lock)
            {
                _slot._exclusiveHeld = false;
                System.Threading.Monitor.PulseAll(_slot._lock);
            }
        }
    }
}
