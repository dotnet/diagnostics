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
/// Governs how many live host instances of one kind may exist at once — here, exactly one.
///
/// Debugger backends need this for different reasons:
/// <list type="bullet">
///   <item><b>cdb (in-process dbgeng)</b> is genuinely one-instance-per-process (a second client
///   throws).</item>
///   <item><b>dotnet-dump</b> children each busy-wait on stdin at ~100% CPU; keeping many alive
///   saturates the machine, so we keep at most one.</item>
///   <item><b>lldb</b> children retain every loaded core and hosted SOS runtime. Keeping one per
///   memoized dump session can exhaust memory during a large run, so dump sessions share one.</item>
/// </list>
/// The most-recently-used host stays open and is evicted (disposed) only when a different target
/// of the same kind is needed — so a run of assertions against one dump reuses the open host, and
/// switching dumps reopens (cheap relative to the work). Live targets take an exclusive lease for
/// their lifetime. This single-slot constraint is exactly what a subprocess-per-target backend
/// would lift, without changing the test-facing API.
/// </summary>
internal sealed class HostSlot
{
    /// <summary>The in-process dbgeng slot (cdb dump hosts and live hosts).</summary>
    public static readonly HostSlot DbgEng = new();

    /// <summary>The dotnet-dump slot (one analyze child alive at a time).</summary>
    public static readonly HostSlot DotNetDump = new();

    /// <summary>The LLDB dump slot (one core-loaded child alive at a time).</summary>
    public static readonly HostSlot Lldb = new();

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
