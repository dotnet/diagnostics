// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.NETCore.Client.UnitTests;
using Xunit;
using Xunit.Abstractions;

namespace DotnetMonitor.UnitTests
{
    public class EndpointInfoSourceTests
    {
        // Generous timeout to allow APIs to respond on slower or more constrained machines
        private static readonly TimeSpan DefaultPositiveVerificationTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DefaultNegativeVerificationTimeout = TimeSpan.FromSeconds(2);

        private readonly ITestOutputHelper _outputHelper;

        public EndpointInfoSourceTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        /// <summary>
        /// Tests that other <see cref="ServerEndpointInfoSource"> methods throw if
        /// <see cref="ServerEndpointInfoSource.Start"/> is not called.
        /// </summary>
        [Fact]
        public async Task ServerSourceNoStartTest()
        {
            await using var source = CreateServerSource(out string transportName);
            // Intentionally do not call Start

            using CancellationTokenSource cancellation = new CancellationTokenSource(DefaultNegativeVerificationTimeout);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => source.GetEndpointInfoAsync(cancellation.Token));
        }

        /// <summary>
        /// Tests that the server endpoint info source has not connections if no processes connect to it.
        /// </summary>
        [Fact]
        public async Task ServerSourceNoConnectionsTest()
        {
            await using var source = CreateServerSource(out _);
            source.Start();

            var endpointInfos = await GetEndpointInfoAsync(source);
            Assert.Empty(endpointInfos);
        }

        /// <summary>
        /// Tests that server endpoint info source should throw ObjectDisposedException
        /// from API surface after being disposed.
        /// </summary>
        [Fact]
        public async Task ServerSourceThrowsWhenDisposedTest()
        {
            var source = CreateServerSource(out _);
            source.Start();

            await source.DisposeAsync();

            // Validate source surface throws after disposal
            Assert.Throws<ObjectDisposedException>(
                () => source.Start());

            Assert.Throws<ObjectDisposedException>(
                () => source.Start(1));

            using var cancellation = new CancellationTokenSource(DefaultNegativeVerificationTimeout);
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => source.GetEndpointInfoAsync(cancellation.Token));
        }

        /// <summary>
        /// Tests that server endpoint info source should throw an exception from
        /// <see cref="ServerEndpointInfoSource.Start"/> and
        /// <see cref="ServerEndpointInfoSource.Start(int)"/> after listening was already started.
        /// </summary>
        [Fact]
        public async Task ServerSourceThrowsWhenMultipleStartTest()
        {
            await using var source = CreateServerSource(out _);
            source.Start();

            Assert.Throws<InvalidOperationException>(
                () => source.Start());

            Assert.Throws<InvalidOperationException>(
                () => source.Start(1));
        }

        /// <summary>
        /// Tests that the server endpoint info source can properly enumerate endpoint infos when a single
        /// target connects to it and "disconnects" from it.
        /// </summary>
        [Fact]
        public async Task ServerSourceAddRemoveSingleConnectionTest()
        {
            await using var source = CreateServerSource(out string transportName);
            source.Start();

            var endpointInfos = await GetEndpointInfoAsync(source);
            Assert.Empty(endpointInfos);

            Task newEndpointInfoTask = source.WaitForNewEndpointInfoAsync(DefaultPositiveVerificationTimeout);

            await using (var execution1 = StartTraceeProcess("LoggerRemoteTest", transportName))
            {
                await newEndpointInfoTask;

                execution1.Start();

                endpointInfos = await GetEndpointInfoAsync(source);

                var endpointInfo = Assert.Single(endpointInfos);
                Assert.NotNull(endpointInfo.CommandLine);
                Assert.NotNull(endpointInfo.OperatingSystem);
                Assert.NotNull(endpointInfo.ProcessArchitecture);
                VerifyConnection(execution1.TestRunner, endpointInfo);

                _outputHelper.WriteLine("Stopping tracee.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1));

            endpointInfos = await GetEndpointInfoAsync(source);

            Assert.Empty(endpointInfos);
        }

        private TestServerEndpointInfoSource CreateServerSource(out string transportName)
        {
            transportName = ReversedServerHelper.CreateServerTransportName();
            _outputHelper.WriteLine("Starting server endpoint info source at '" + transportName + "'.");
            return new TestServerEndpointInfoSource(transportName, _outputHelper);
        }

        private RemoteTestExecution StartTraceeProcess(string loggerCategory, string transportName = null)
        {
            _outputHelper.WriteLine("Starting tracee.");
            string exePath = CommonHelper.GetTraceePathWithArgs("EventPipeTracee", targetFramework: "net5.0");
            return RemoteTestExecution.StartProcess(exePath + " " + loggerCategory, _outputHelper, transportName);
        }

        private async Task<IEnumerable<IEndpointInfo>> GetEndpointInfoAsync(ServerEndpointInfoSource source)
        {
            _outputHelper.WriteLine("Getting endpoint infos.");
            using CancellationTokenSource cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await source.GetEndpointInfoAsync(cancellationSource.Token);
        }

        /// <summary>
        /// Verifies basic information on the connection and that it matches the target process from the runner.
        /// </summary>
        private static void VerifyConnection(TestRunner runner, IEndpointInfo endpointInfo)
        {
            Assert.NotNull(runner);
            Assert.NotNull(endpointInfo);
            Assert.Equal(runner.Pid, endpointInfo.ProcessId);
            Assert.NotEqual(Guid.Empty, endpointInfo.RuntimeInstanceCookie);
            Assert.NotNull(endpointInfo.Endpoint);
        }

        private sealed class TestServerEndpointInfoSource : ServerEndpointInfoSource
        {
            private readonly ITestOutputHelper _outputHelper;
            private readonly List<TaskCompletionSource<EndpointInfo>> _addedEndpointInfoSources = new List<TaskCompletionSource<EndpointInfo>>();

            public TestServerEndpointInfoSource(string transportPath, ITestOutputHelper outputHelper)
                : base(transportPath)
            {
                _outputHelper = outputHelper;
            }

            public async Task<EndpointInfo> WaitForNewEndpointInfoAsync(TimeSpan timeout)
            {
                TaskCompletionSource<EndpointInfo> addedEndpointInfoSource = new TaskCompletionSource<EndpointInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var timeoutCancellation = new CancellationTokenSource();
                var token = timeoutCancellation.Token;
                using var _ = token.Register(() => addedEndpointInfoSource.TrySetCanceled(token));

                lock (_addedEndpointInfoSources)
                {
                    _addedEndpointInfoSources.Add(addedEndpointInfoSource);
                }

                _outputHelper.WriteLine("Waiting for new endpoint info.");
                timeoutCancellation.CancelAfter(timeout);
                EndpointInfo endpointInfo = await addedEndpointInfoSource.Task;
                _outputHelper.WriteLine("Notified of new endpoint info.");

                return endpointInfo;
            }

            internal override void OnAddedEndpointInfo(EndpointInfo info)
            {
                _outputHelper.WriteLine($"Added endpoint info to collection: {info.DebuggerDisplay}");
                
                lock (_addedEndpointInfoSources)
                {
                    foreach (var source in _addedEndpointInfoSources)
                    {
                        source.TrySetResult(info);
                    }
                    _addedEndpointInfoSources.Clear();
                }
            }

            internal override void OnRemovedEndpointInfo(EndpointInfo info)
            {
                _outputHelper.WriteLine($"Removed endpoint info from collection: {info.DebuggerDisplay}");
            }
        }
    }
}
