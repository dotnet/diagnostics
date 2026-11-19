// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;
using Xunit.Sdk;

namespace SOS.Tests;

/// <summary>Owns one stopped session until all selected cases in its collection have finished.</summary>
public sealed class DumpCollectionFixture : IAsyncDisposable
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly Func<TestConfig, string, CancellationToken, Task<DumpSession>> _openSession;
    private DumpSession? _session;
    private string? _key;
    private bool _disposed;

    public DumpCollectionFixture()
        : this(OpenSessionAsync)
    {
    }

    internal DumpCollectionFixture(Func<TestConfig, string, CancellationToken, Task<DumpSession>> openSession)
    {
        _openSession = openSession;
    }

    public static async Task<Target> GetTargetAsync(TestConfig config, string? stopName = null)
    {
        ITestCollection? collection = TestContext.Current.TestCollection;
        if (config.IsLive || collection is null ||
            collection.TestCollectionClassName != typeof(DumpCollectionDefinition).FullName)
        {
            return await Targets.GetTargetAsync(config);
        }

        DumpCollectionFixture fixture = await TestContext.Current.GetFixture<DumpCollectionFixture>()
            ?? throw new InvalidOperationException("The dump collection is missing its session fixture.");
        string stop = stopName ?? TargetCatalog.Get(config.Target).DefaultStopName;
        if (collection.TestCollectionDisplayName != SosDumpTheoryAttribute.CollectionName(config, stop))
        {
            throw new InvalidOperationException("The requested dump does not match this collection's session key.");
        }

        ReplayContext.Start(config, live: false);
        return await fixture.GetTargetCoreAsync(config, stop, TestContext.Current.CancellationToken);
    }

    internal async Task<Target> GetTargetCoreAsync(TestConfig config, string stop, CancellationToken cancellationToken)
    {
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string key = SosDumpTheoryAttribute.CollectionName(config, stop);
            if (_key is not null && _key != key)
            {
                throw new InvalidOperationException("A collection fixture cannot switch dump sessions.");
            }

            _key = key;
            _session ??= await _openSession(config, stop, cancellationToken);
            return new DeadTarget(config, _session);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task<DumpSession> OpenSessionAsync(TestConfig config, string stop, CancellationToken cancellationToken)
    {
        // Capture before reserving a debugger slot: capture itself may need debugger resources.
        string dump = await Task.Run(() => SnapshotStore.GetDump(
            config.Flavor, config.Target, stop, config.GcType, config.DumpKind, config.CoreVersion),
            cancellationToken);
        HostSlotPool.Reservation reservation = await HostSlotPool.Shared.ReserveAsync(cancellationToken: cancellationToken);
        try
        {
            return new DumpSession(config.Host, config.Target, stop, config.Flavor,
                dump, config.CoreVersion, config.Dac, config.HostRuntime, reservation);
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _initializationGate.WaitAsync();
        try
        {
            if (!_disposed)
            {
                _disposed = true;
                _session?.CloseReservedSession();
                _session = null;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }
}
