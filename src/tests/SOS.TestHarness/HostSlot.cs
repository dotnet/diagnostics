// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.Json;

namespace SOS.TestHarness;

internal enum SessionKind
{
    Dump,
    Live,
    Capture,
    Probe,
}

/// <summary>
/// A session whose debugger hosts are created on demand and owned by individual slots.
/// </summary>
internal interface IPooledHost
{
    IDebuggerHost CreateHost();
}

/// <summary>
/// Shares a backend-neutral budget between dump analyzers, live targets, and capture workloads.
/// Unreserved dump hosts stay cached, but any kind of session can evict them to obtain a slot.
/// </summary>
internal sealed class HostSlotPool
{
    private static readonly object s_sharedGate = new();
    private static HostSlotPool? s_shared;

    public static HostSlotPool Shared
    {
        get
        {
            lock (s_sharedGate)
            {
                return s_shared ??= new(DefaultCapacity);
            }
        }
    }

    internal static int DefaultCapacity => Math.Min(8, Environment.ProcessorCount);

    private readonly HostSlot[] _slots;
    private readonly HashSet<HostSlot> _reserved = [];
    private readonly object _gate = new();
    private readonly SemaphoreSlim _available;
    private readonly long[] _acquisitions = new long[Enum.GetValues<SessionKind>().Length];
    private readonly long[] _waitTicks = new long[Enum.GetValues<SessionKind>().Length];
    private long _clock;
    private long _cacheHits;
    private long _evictions;
    private int _peakReserved;

    public int Capacity => _slots.Length;

    static HostSlotPool()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                string statistics = JsonSerializer.Serialize(Shared.GetStatistics());
                Console.WriteLine("SOS session budget: " + statistics);
                Directory.CreateDirectory(RepoLayout.UploadRoot);
                File.WriteAllText(Path.Combine(RepoLayout.UploadRoot, "session-budget.json"), statistics);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not write SOS session statistics: {ex.Message}");
            }
            finally
            {
                Shared.CloseAll();
            }
        };
    }

    public HostSlotPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _available = new SemaphoreSlim(capacity, capacity);
        _slots = new HostSlot[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _slots[i] = new HostSlot();
        }
    }

    internal static void ConfigureShared(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        lock (s_sharedGate)
        {
            if (s_shared is not null && s_shared.Capacity != capacity)
            {
                throw new InvalidOperationException("The shared session limit must be configured before the pool is used.");
            }
            s_shared ??= new(capacity);
        }
    }

    public Reservation Reserve(SessionKind kind = SessionKind.Dump, IPooledHost? owner = null, CancellationToken cancellationToken = default) =>
        ReserveAsync(kind, owner, cancellationToken).GetAwaiter().GetResult();

    public async Task<Reservation> ReserveAsync(SessionKind kind = SessionKind.Dump, IPooledHost? owner = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (kind != SessionKind.Dump && owner is not null)
        {
            throw new ArgumentException("Only dump reservations can reuse a cached host.", nameof(owner));
        }

        long start = Stopwatch.GetTimestamp();
        await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
        Reservation? reservation = null;
        try
        {
            bool cacheHit;
            lock (_gate)
            {
                HostSlot[] available = _slots.Where(slot => !_reserved.Contains(slot)).ToArray();
                HostSlot slot = (owner is null ? null : available.FirstOrDefault(slot => ReferenceEquals(slot.Owner, owner)))
                    ?? available.FirstOrDefault(slot => slot.Owner is null)
                    ?? available.MinBy(slot => slot.LastUsed)!;
                cacheHit = owner is not null && ReferenceEquals(slot.Owner, owner);
                _cacheHits += cacheHit ? 1 : 0;
                _evictions += !cacheHit && slot.Owner is not null ? 1 : 0;
                _reserved.Add(slot);
                _peakReserved = Math.Max(_peakReserved, _reserved.Count);
                _acquisitions[(int)kind]++;
                _waitTicks[(int)kind] += Stopwatch.GetTimestamp() - start;
                reservation = new Reservation(this, slot);
            }

            // Close outside the pool lock, but before giving the slot to a new workload.
            if (!cacheHit)
            {
                reservation.Slot.CloseCurrent();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return reservation;
        }
        catch
        {
            if (reservation is not null)
            {
                reservation.Dispose();
            }
            else
            {
                _available.Release();
            }
            throw;
        }
    }

    private void Release(HostSlot slot)
    {
        lock (_gate)
        {
            slot.LastUsed = ++_clock;
            _reserved.Remove(slot);
        }

        _available.Release();
    }

    internal Statistics GetStatistics()
    {
        lock (_gate)
        {
            return new Statistics(Environment.ProcessorCount, Capacity, _reserved.Count,
                _slots.Count(slot => !_reserved.Contains(slot) && slot.Owner is not null),
                _peakReserved, _cacheHits, _evictions,
                Enum.GetValues<SessionKind>().ToDictionary(kind => kind,
                    kind => new Usage(_acquisitions[(int)kind], (double)_waitTicks[(int)kind] / Stopwatch.Frequency)));
        }
    }

    internal sealed record Usage(long Acquisitions, double WaitSeconds);
    internal sealed record Statistics(int ProcessorCount, int Capacity, int Reserved, int IdleRetained,
        int PeakReserved, long CacheHits, long Evictions, IReadOnlyDictionary<SessionKind, Usage> Sessions);

    /// <summary>A thread-independent reservation; command serialization remains the slot's responsibility.</summary>
    public sealed class Reservation(HostSlotPool pool, HostSlot slot) : IDisposable
    {
        private HostSlotPool? _pool = pool;

        public HostSlot Slot { get; } = slot;

        public void Dispose() => Interlocked.Exchange(ref _pool, null)?.Release(Slot);
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
/// Dump analyzers retain loaded state while idle; reservations can evict them for any session kind.
/// The host belongs to the slot, not its logical session: an old copy can finish closing here while
/// another slot opens a replacement for the same session.
/// </summary>
internal sealed class HostSlot
{
    private readonly object _lock = new();
    private volatile IPooledHost? _open;
    private IDebuggerHost? _host;

    internal IPooledHost? Owner => _open;
    internal long LastUsed { get; set; }
    internal IDebuggerHost Host => _host ?? throw new InvalidOperationException("The slot has no open host.");

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
                CloseCurrent();
                try
                {
                    _host = owner.CreateHost();
                    _host.LoadSos();
                }
                catch (Exception openException)
                {
                    try
                    {
                        CloseCurrent();
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
            IDebuggerHost? host = _host;
            _open = null;
            _host = null;
            host?.Dispose();
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
