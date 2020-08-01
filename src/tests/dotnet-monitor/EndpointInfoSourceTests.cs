// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.NETCore.Client;
using Xunit;
using Xunit.Abstractions;

namespace DotnetMonitor.UnitTests
{
    public class EndpointInfoSourceTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public EndpointInfoSourceTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        /// <summary>
        /// Tests that the server endpoint info source has no connections
        /// if <see cref="ServerEndpointInfoSource.Listen"/> is not called.
        /// </summary>
        [Fact]
        public async Task ServerSourceNoListenTest()
        {
            await using var source = CreateServerSource(out string transportName);
            // Intentionally do not call Listen

            await using (var execution1 = StartTraceeProcess("LoggerRemoteTest", transportName))
            {
                execution1.Start();

                await Task.Delay(TimeSpan.FromSeconds(1));

                var endpointInfos = await GetEndpointInfoAsync(source);

                Assert.Empty(endpointInfos);

                _outputHelper.WriteLine("Stopping tracee.");
            }
        }

        /// <summary>
        /// Tests that the server endpoint info source has not connections if no processes connect to it.
        /// </summary>
        [Fact]
        public async Task ServerSourceNoConnectionsTest()
        {
            await using var source = CreateServerSource(out _);
            source.Listen();

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
            source.Listen();

            await source.DisposeAsync();

            // Validate source surface throws after disposal
            Assert.Throws<ObjectDisposedException>(
                () => source.Listen());

            Assert.Throws<ObjectDisposedException>(
                () => source.Listen(1));

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => source.GetEndpointInfoAsync(CancellationToken.None));
        }

        /// <summary>
        /// Tests that server endpoint info source should throw an exception from
        /// <see cref="ServerEndpointInfoSource.Listen"/> and
        /// <see cref="ServerEndpointInfoSource.Listen(int)"/> after listening was already started.
        /// </summary>
        [Fact]
        public async Task ServerSourceThrowsWhenMultipleListenTest()
        {
            await using var source = CreateServerSource(out _);
            source.Listen();

            Assert.Throws<InvalidOperationException>(
                () => source.Listen());

            Assert.Throws<InvalidOperationException>(
                () => source.Listen(1));
        }

        /// <summary>
        /// Tests that the server endpoint info source can properly enumerate endpoint infos when a single
        /// target connects to it and "disconnects" from it.
        /// </summary>
        [Fact]
        public async Task ServerSourceAddRemoveSingleConnectionTest()
        {
            await using var source = CreateServerSource(out string transportName);
            source.Listen();

            var endpointInfos = await GetEndpointInfoAsync(source);
            Assert.Empty(endpointInfos);

            using var newEndpointInfoHelper = new NewEndpointInfoHelper(source, _outputHelper);

            await using (var execution1 = StartTraceeProcess("LoggerRemoteTest", transportName))
            {
                await newEndpointInfoHelper.WaitForNewEndpointInfoAsync(TimeSpan.FromSeconds(5));

                execution1.Start();

                endpointInfos = await GetEndpointInfoAsync(source);

                var endpointInfo = Assert.Single(endpointInfos);
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
            string exePath = CommonHelper.GetTraceePath("EventPipeTracee", targetFramework: "net5.0");
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

        /// <summary>
        /// Helper class for waiting for notification of a new connection from the connection source.
        /// This aids in allowing to wait for a new connection with a timeout rather than waiting
        /// for a specified amount of time and then testing for the new connection (and possibly repeating
        /// if a new connection was not found).
        /// </summary>
        private sealed class NewEndpointInfoHelper : IDisposable
        {
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly EventTaskSource<EventHandler> _newEndpointInfoSource;
            private readonly ITestOutputHelper _outputHelper;

            private bool _disposed;

            public NewEndpointInfoHelper(TestServerEndpointInfoSource source, ITestOutputHelper outputHelper)
            {
                // Create a task source that is signaled
                // when the AddedEndpointInfo event is raise.
                _newEndpointInfoSource = new EventTaskSource<EventHandler>(
                    complete => (s, e) => complete(),
                    h => source.AddedEndpointInfo += h,
                    h => source.AddedEndpointInfo -= h,
                    _cancellation.Token);
                _outputHelper = outputHelper;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _cancellation.Cancel();
                    _cancellation.Dispose();

                    _disposed = true;
                }
            }

            public async Task WaitForNewEndpointInfoAsync(TimeSpan timeout)
            {
                _outputHelper.WriteLine("Waiting for new endpoint info.");
                _cancellation.CancelAfter(timeout);
                await _newEndpointInfoSource.Task;
                _outputHelper.WriteLine("Notified of new endpoint info.");
            }
        }

        private sealed class TestServerEndpointInfoSource : ServerEndpointInfoSource
        {
            private readonly ITestOutputHelper _outputHelper;

            public TestServerEndpointInfoSource(string transportPath, ITestOutputHelper outputHelper)
                : base(transportPath)
            {
                _outputHelper = outputHelper;
            }

            internal override void OnAddedEndpointInfo(IpcEndpointInfo info)
            {
                _outputHelper.WriteLine($"Added endpoint info to collection: {info.ToTestString()}");
                AddedEndpointInfo(this, EventArgs.Empty);
            }

            internal override void OnRemovedEndpointInfo(IpcEndpointInfo info)
            {
                _outputHelper.WriteLine($"Removed endpoint info from collection: {info.ToTestString()}");
            }

            public event EventHandler AddedEndpointInfo;
        }
    }
}
