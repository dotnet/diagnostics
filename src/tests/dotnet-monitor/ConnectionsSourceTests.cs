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
    public class ConnectionsSourceTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public ConnectionsSourceTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        /// <summary>
        /// Tests that the server connections source has not connections if no processes connect to it.
        /// </summary>
        [Fact]
        public async Task ServerConnectionsSourceNoConnectionsTest()
        {
            await using var source = StartReversedServerConnectionsSource(out _);

            var connections = await GetConnectionsAsync(source);
            Assert.Empty(connections);
        }

        /// <summary>
        /// Tests that the server connection source can properly enumerate connections when a single
        /// target connects to it and "disconnects" from it.
        /// </summary>
        [Fact]
        public async Task ServerConnectionsSourceAddRemoveSingleConnectionTest()
        {
            await using var source = StartReversedServerConnectionsSource(out string transportName);

            var connections = await GetConnectionsAsync(source);
            Assert.Empty(connections);

            using var newConnectionHelper = new NewConnectionHelper(source, _outputHelper);

            await using (var execution1 = StartTraceeProcess("LoggerRemoteTest", transportName))
            {
                await newConnectionHelper.WaitForNewConnectionAsync(TimeSpan.FromSeconds(5));

                execution1.Start();

                connections = await GetConnectionsAsync(source);

                var connection1 = Assert.Single(connections);
                VerifyConnection(execution1.TestRunner, connection1);

                _outputHelper.WriteLine("Stopping tracee.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3));

            connections = await GetConnectionsAsync(source);

            Assert.Empty(connections);
        }

        private ReversedServerConnectionsSource StartReversedServerConnectionsSource(out string transportName)
        {
            transportName = ReversedServerHelper.CreateServerTransportName();
            _outputHelper.WriteLine("Starting reversed connections source at '" + transportName + "'.");
            return new ReversedServerConnectionsSource(transportName);
        }

        private RemoteTestExecution StartTraceeProcess(string loggerCategory, string transportName = null)
        {
            _outputHelper.WriteLine("Starting tracee.");
            string exePath = CommonHelper.GetTraceePath("EventPipeTracee", targetFramework: "net5.0");
            return RemoteTestExecution.StartProcess(exePath + " " + loggerCategory, _outputHelper, transportName);
        }

        private async Task<IEnumerable<IDiagnosticsConnection>> GetConnectionsAsync(ReversedServerConnectionsSource source)
        {
            _outputHelper.WriteLine("Getting connections.");
            using CancellationTokenSource cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await source.GetConnectionsAsync(cancellationSource.Token);
        }

        /// <summary>
        /// Verifies basic information on the connection and that it matches the target process from the runner.
        /// </summary>
        private static void VerifyConnection(TestRunner runner, IDiagnosticsConnection connection)
        {
            Assert.NotNull(runner);
            Assert.NotNull(connection);
            Assert.Equal(runner.Pid, connection.ProcessId);
            Assert.NotEqual(Guid.Empty, connection.RuntimeInstanceCookie);
            Assert.NotNull(connection.Endpoint);
        }

        /// <summary>
        /// Helper class for waiting for notification of a new connection from the connection source.
        /// This aids in allowing to wait for a new connection with a timeout rather than waiting
        /// for a specified amount of time and then testing for the new connection (and possibly repeating
        /// if a new connection was not found).
        /// </summary>
        private sealed class NewConnectionHelper : IDisposable
        {
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly Task _newConnectionTask;
            private readonly ITestOutputHelper _outputHelper;

            private bool _disposed;

            public NewConnectionHelper(ReversedServerConnectionsSource source, ITestOutputHelper outputHelper)
            {
                _newConnectionTask = source.WaitForNewConnectionAsync(_cancellation.Token);
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

            public async Task WaitForNewConnectionAsync(TimeSpan timeout)
            {
                _outputHelper.WriteLine("Waiting for new connection.");
                _cancellation.CancelAfter(timeout);
                await _newConnectionTask;
                _outputHelper.WriteLine("Notified of new connection.");
            }
        }
    }
}
