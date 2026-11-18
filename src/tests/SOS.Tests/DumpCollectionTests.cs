// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;
using SOS.TestHarness;
using Xunit;
using Xunit.Runner.Common;
using Xunit.Sdk;
using Xunit.v3;

namespace SOS.Tests;

public sealed class DumpCollectionTests
{
    [Fact]
    public async Task MatchingRowsFromDifferentClassesShareCollectionAndKeepTheirIdentity()
    {
        TestConfig config = Config();
        IXunitTestCase first = await CreateRow(typeof(ClrStackTests), nameof(ClrStackTests.ClrStack_Registers), config);
        IXunitTestCase second = await CreateRow(typeof(ClrThreadsTests), nameof(ClrThreadsTests.ClrThreads_ReportsThreadCount), config);

        Assert.Equal(first.TestCollection.UniqueID, second.TestCollection.UniqueID);
        Assert.NotEqual(first.UniqueID, second.UniqueID);
        Assert.Equal(typeof(ClrStackTests), first.TestMethod.TestClass.Class);
        Assert.Equal(typeof(ClrThreadsTests), second.TestMethod.TestClass.Class);
        Assert.Contains(typeof(DumpCollectionFixture), first.TestCollection.CollectionFixtureTypes);
    }

    [Fact]
    public async Task EverySessionAxisAndStopSeparatesCollections()
    {
        TestConfig config = Config();
        TestConfig[] configurations =
        [
            config,
            config with { Target = TargetCatalog.NestedException },
            config with { Host = Host.Cdb },
            config with { Flavor = Flavor.SingleFile },
            config with { GcType = GcType.Server },
            config with { DumpKind = DumpKind.Mini },
            config with { CoreVersion = CoreVersion.Net11 },
            config with { Dac = Dac.CDac },
            config with { HostRuntime = HostRuntime.None },
        ];
        HashSet<string> ids = [];
        foreach (TestConfig candidate in configurations)
        {
            IXunitTestCase testCase = await CreateRow(typeof(ClrStackTests), nameof(ClrStackTests.ClrStack_Registers), candidate);
            Assert.True(ids.Add(testCase.TestCollection.UniqueID));
        }

        IXunitTestCase otherStop = await CreateRow(
            typeof(ClrStackTests), nameof(ClrStackTests.ClrStack_GcRoots), config, TargetCatalog.StopRoots);
        Assert.True(ids.Add(otherStop.TestCollection.UniqueID));
    }

    [Fact]
    public async Task RowMetadataAndCollectionSurviveSerialization()
    {
        TestConfig config = Config();
        TheoryDataRow<TestConfig> row = new(config)
        {
            Skip = "row skip",
            Timeout = 1234,
            Traits = new() { ["category"] = ["retained"] },
        };
        IXunitTestCase original = Assert.Single(await new TestDiscoverer().Create(
            Method(typeof(ClrStackTests), nameof(ClrStackTests.ClrStack_Registers)),
            row, [config]));
        string serialized = SerializationHelper.Instance.Serialize(original);
        IXunitTestCase restored = Assert.IsAssignableFrom<IXunitTestCase>(
            SerializationHelper.Instance.Deserialize<IXunitTestCase>(serialized));

        Assert.Equal(original.UniqueID, restored.UniqueID);
        Assert.Equal(original.TestCollection.UniqueID, restored.TestCollection.UniqueID);
        Assert.Contains(typeof(DumpCollectionFixture), restored.TestCollection.CollectionFixtureTypes);
        Assert.Equal("row skip", restored.SkipReason);
        Assert.Equal(1234, restored.Timeout);
        Assert.Equal("retained", Assert.Single(restored.Traits["category"]));
        Assert.Equal(config, restored.TestMethod.TestMethodArguments[0]);
    }

    [Fact]
    public async Task LiveRowsAndDeferredTheoriesKeepTheirOrdinaryCollection()
    {
        IXunitTestMethod method = Method(typeof(ClrStackTests), nameof(ClrStackTests.ClrStack_Registers));
        TestConfig live = Config() with { Host = Host.Cdb, Liveness = Liveness.Live };
        IXunitTestCase liveCase = Assert.Single(await new TestDiscoverer().Create(
            method, new TheoryDataRow<TestConfig>(live), [live]));
        Assert.Equal(method.TestClass.TestCollection.UniqueID, liveCase.TestCollection.UniqueID);
        Assert.DoesNotContain(typeof(DumpCollectionFixture), liveCase.TestCollection.CollectionFixtureTypes);

        ITestFrameworkDiscoveryOptions options = TestFrameworkOptions.ForDiscovery(new TestAssemblyConfiguration());
        options.SetPreEnumerateTheories(false);
        IXunitTestCase deferred = Assert.Single(await new SosDumpTheoryDiscoverer().Discover(
            options, method, new SosDumpTheoryAttribute()));
        Assert.IsType<XunitDelayEnumeratedTheoryTestCase>(deferred);
        Assert.Equal(method.TestClass.TestCollection.UniqueID, deferred.TestCollection.UniqueID);
    }

    [Fact]
    public void CollectionCursorRejectsOtherStopsWithoutOpeningADebugger()
    {
        TestConfig config = Config();
        DumpSession session = new(config.Host, config.Target, TargetCatalog.StopHeap, config.Flavor, "unused");
        using DeadTarget target = new(config, session);

        target.GoToStopPoint(TargetCatalog.StopHeap);
        Assert.Equal("unused", target.DumpPath);
        Assert.Throws<InvalidOperationException>(() => target.GoToStopPoint(TargetCatalog.StopRoots));
    }

    [Fact]
    public async Task ConcurrentInitializationReusesSessionAndCancelledWaiterDoesNotInterfere()
    {
        HostSlotPool pool = new(capacity: 1);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int opens = 0;
        await using DumpCollectionFixture fixture = new(async (config, stop, token) => {
            Interlocked.Increment(ref opens);
            entered.SetResult();
            await proceed.Task.WaitAsync(token);
            return await OpenFakeSession(pool, config, stop, token);
        });
        CancellationToken token = TestContext.Current.CancellationToken;
        Task<Target> first = fixture.GetTargetCoreAsync(Config(), TargetCatalog.StopHeap, token);
        await entered.Task.WaitAsync(token);
        Task<Target> second = fixture.GetTargetCoreAsync(Config(), TargetCatalog.StopHeap, token);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<Target> cancelled = fixture.GetTargetCoreAsync(Config(), TargetCatalog.StopHeap, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        proceed.SetResult();

        using Target firstTarget = await first;
        using Target secondTarget = await second;
        firstTarget.GoToStopPoint(TargetCatalog.StopHeap);
        secondTarget.GoToStopPoint(TargetCatalog.StopHeap);
        Assert.Equal(1, opens);
        Assert.Equal(firstTarget.DumpPath, secondTarget.DumpPath);

        await fixture.DisposeAsync();
        using HostSlotPool.Reservation returned = await pool.ReserveAsync(cancellationToken: token)
            .WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    [Fact]
    public async Task FailedInitializationCanRetryAndDisposalPreventsReopening()
    {
        HostSlotPool pool = new(capacity: 1);
        int attempts = 0;
        await using DumpCollectionFixture fixture = new((config, stop, token) =>
            ++attempts == 1
                ? Task.FromException<DumpSession>(new InvalidOperationException("initialization failed"))
                : OpenFakeSession(pool, config, stop, token));
        CancellationToken token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.GetTargetCoreAsync(Config(), TargetCatalog.StopHeap, token));
        using Target target = await fixture.GetTargetCoreAsync(Config(), TargetCatalog.StopHeap, token);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.GetTargetCoreAsync(Config(), TargetCatalog.StopRoots, token));
        Assert.Equal(2, attempts);

        await fixture.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => fixture.GetTargetCoreAsync(Config(), TargetCatalog.StopHeap, token));
        using HostSlotPool.Reservation returned = await pool.ReserveAsync(cancellationToken: token)
            .WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ConservativeSchedulingLetsSlotOwnersFinishTheirCollections(int maxParallelThreads)
    {
        SchedulingFixture.Pool = new HostSlotPool(capacity: 1);
        XunitTestAssembly assembly = new(typeof(DumpCollectionTests).Assembly);
        List<IXunitTestCase> cases = [];
        foreach (string name in new[] { "first", "second" })
        {
            XunitTestCollection collection = new(assembly, typeof(SchedulingCollection), false, name);
            foreach (string methodName in new[] { nameof(SchedulingCases.First), nameof(SchedulingCases.Second) })
            {
                AddCase(collection, methodName);
            }
        }
        AddCase(new XunitTestCollection(assembly, null, false, "ordinary"), nameof(SchedulingCases.Ordinary));
        ITestFrameworkExecutionOptions options = TestFrameworkOptions.ForExecution(new TestAssemblyConfiguration());
        options.SetMaxParallelThreads(maxParallelThreads);
        options.SetParallelAlgorithm(ParallelAlgorithm.Conservative);
        options.SetDisableParallelization(false);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(10));
        ErrorSink sink = new();
        RunSummary summary = await XunitTestAssemblyRunner.Instance.Run(assembly, cases, sink, options, cancellation.Token);

        Assert.Empty(sink.Errors);
        Assert.Equal(5, summary.Total);
        Assert.Equal(0, summary.Failed);
        using HostSlotPool.Reservation returned = await SchedulingFixture.Pool.ReserveAsync(
            cancellationToken: TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        void AddCase(IXunitTestCollection collection, string methodName)
        {
            XunitTestClass testClass = new(typeof(SchedulingCases), collection);
            XunitTestMethod method = new(testClass, typeof(SchedulingCases).GetMethod(methodName)!, []);
            cases.Add(new XunitTestCase(method, methodName, collection.UniqueID + methodName, @explicit: false));
        }
    }

    [Fact]
    public async Task DiscoveryHonorsTargetSpecificStopsAndWindowsRows()
    {
        TestDiscoverer discoverer = new();
        SosDumpTheoryAttribute attribute = new()
        {
            StopName = TargetCatalog.StopArgsLocals,
            ForTarget = TargetCatalog.Scenarios,
        };
        foreach (TestConfig config in new[] { Config(), Config() with { Target = TargetCatalog.DivZero } })
        {
            IXunitTestCase row = Assert.Single(await discoverer.Create(
                Method(typeof(ClrStackLinesTests), nameof(ClrStackLinesTests.ClrStack_SourceLines)),
                new TheoryDataRow<TestConfig>(config), [config], attribute));
            string stop = config.Target == TargetCatalog.Scenarios ? TargetCatalog.StopArgsLocals : "crash";
            Assert.Equal(SosDumpTheoryAttribute.CollectionName(config, stop), row.TestCollection.TestCollectionDisplayName);
        }

        ITestFrameworkDiscoveryOptions options = TestFrameworkOptions.ForDiscovery(new TestAssemblyConfiguration());
        options.SetPreEnumerateTheories(true);
        IReadOnlyCollection<IXunitTestCase> windowsRows = await new WindowsDumpTheoryDiscoverer().Discover(
            options, Method(typeof(ClrThreadsTests), nameof(ClrThreadsTests.ClrThreads_ReportsApartmentStates)),
            new WindowsDumpTheoryAttribute());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Empty(windowsRows);
        }
        else
        {
            Assert.All(windowsRows.Where(row => row.TestMethod.TestMethodArguments.FirstOrDefault() is TestConfig),
                row => Assert.Contains(typeof(DumpCollectionFixture), row.TestCollection.CollectionFixtureTypes));
        }
    }

    private static async Task<DumpSession> OpenFakeSession(HostSlotPool pool, TestConfig config, string stop, CancellationToken token)
    {
        HostSlotPool.Reservation reservation = await pool.ReserveAsync(cancellationToken: token);
        return new DumpSession(config.Host, config.Target, stop, config.Flavor, "unused", reservation: reservation);
    }

    public sealed class SchedulingCollection : ICollectionFixture<SchedulingFixture>
    {
    }

    public sealed class SchedulingFixture : IAsyncDisposable
    {
        internal static HostSlotPool Pool { get; set; } = new(capacity: 1);
        internal DumpCollectionFixture Fixture { get; } = new(
            (config, stop, token) => OpenFakeSession(Pool, config, stop, token));

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }

    public sealed class SchedulingCases
    {
        public Task First() => UseShared();
        public Task Second() => UseShared();

        private static async Task UseShared()
        {
            SchedulingFixture fixture = Assert.IsType<SchedulingFixture>(
                await TestContext.Current.GetFixture<SchedulingFixture>());
            using Target target = await fixture.Fixture.GetTargetCoreAsync(
                Config(), TargetCatalog.StopHeap, TestContext.Current.CancellationToken);
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        public void Ordinary()
        {
            using HostSlotPool.Reservation reservation = SchedulingFixture.Pool.Reserve(
                SchedulingFixture.Pool.Select(), TestContext.Current.CancellationToken);
        }
    }

    private sealed class ErrorSink : IMessageSink
    {
        public ConcurrentQueue<string> Errors { get; } = new();

        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is IErrorMetadata error)
            {
                Errors.Enqueue(string.Join(Environment.NewLine, error.Messages));
            }
            return true;
        }
    }

    private static TestConfig Config() =>
        new(TargetCatalog.Scenarios, Host.DotnetDump, Flavor.Core, Liveness.Dump);

    private static async Task<IXunitTestCase> CreateRow(Type type, string name, TestConfig config, string? stop = null)
    {
        object?[] arguments = stop is null ? [config] : [config, stop];
        return Assert.Single(await new TestDiscoverer().Create(
            Method(type, name), new TheoryDataRow(arguments), arguments));
    }

    private static IXunitTestMethod Method(Type type, string name)
    {
        XunitTestAssembly assembly = new(typeof(DumpCollectionTests).Assembly);
        XunitTestCollection collection = new(assembly, null, false, type.FullName!);
        XunitTestClass testClass = new(type, collection);
        return new XunitTestMethod(testClass, type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance)!, []);
    }

    private sealed class TestDiscoverer : SosDumpTheoryDiscoverer
    {
        public ValueTask<IReadOnlyCollection<IXunitTestCase>> Create(
            IXunitTestMethod method, ITheoryDataRow row, object?[] arguments, SosDumpTheoryAttribute? attribute = null) =>
            CreateTestCasesForDataRow(
                TestFrameworkOptions.ForDiscovery(new TestAssemblyConfiguration()), method, attribute ?? new SosDumpTheoryAttribute(), row, arguments);
    }
}
