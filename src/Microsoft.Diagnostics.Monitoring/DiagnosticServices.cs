// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FastSerialization;
using Graphs;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class DiagnosticServices : IDiagnosticServices
    {
        private const int DockerEntrypointProcessId = 1;

        private readonly IDiagnosticsConnectionsSourceInternal _connectionsSource;
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public DiagnosticServices(IDiagnosticsConnectionsSource connectionsSource)
        {
            _connectionsSource = (IDiagnosticsConnectionsSourceInternal)connectionsSource;
        }

        public async Task<IEnumerable<IProcessInfo>> GetProcessesAsync(CancellationToken token)
        {
            try
            {
                var connections = await _connectionsSource.GetConnectionsAsync(token);

                return connections.Select(c => new ProcessInfo(c.RuntimeInstanceCookie, c.ProcessId));
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Unable to enumerate processes.");
            }
        }

        public async Task<Stream> GetDump(int pid, DumpType mode, CancellationToken token)
        {
            string dumpFilePath = Path.Combine(Path.GetTempPath(), FormattableString.Invariant($"{Guid.NewGuid()}_{pid}"));
            NETCore.Client.DumpType dumpType = MapDumpType(mode);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Get the process
                Process process = Process.GetProcessById(pid);
                await Dumper.CollectDumpAsync(process, dumpFilePath, dumpType);
            }
            else
            {
                var client = await GetClientAsync(pid, CancellationToken.None);
                await Task.Run(() =>
                {
                    client.WriteDump(dumpType, dumpFilePath);
                });
            }

            return new AutoDeleteFileStream(dumpFilePath);
        }

        public async Task<Stream> GetGcDump(int pid, CancellationToken token)
        {
            var graph = new MemoryGraph(50_000);
            await using var processor = new DiagnosticsEventPipeProcessor(
                PipeMode.GCDump,
                gcGraph: graph);

            var client = await GetClientAsync(pid, token);
            await processor.Process(client, pid, Timeout.InfiniteTimeSpan, token);

            var dumper = new GCHeapDump(graph);
            dumper.CreationTool = "dotnet-monitor";

            var stream = new MemoryStream();
            var serializer = new Serializer(stream, dumper, leaveOpen: true);
            serializer.Close();

            stream.Position = 0;
            return stream;
        }

        public async Task<IStreamWithCleanup> StartTrace(int pid, MonitoringSourceConfiguration configuration, TimeSpan duration, CancellationToken token)
        {
            DiagnosticsMonitor monitor = new DiagnosticsMonitor(configuration);
            var client = await GetClientAsync(pid, token);
            Stream stream = await monitor.ProcessEvents(client, duration, token);
            return new StreamWithCleanup(monitor, stream);
        }

        public async Task StartLogs(Stream outputStream, int pid, TimeSpan duration, LogFormat format, LogLevel level, CancellationToken token)
        {
            using var loggerFactory = new LoggerFactory();

            loggerFactory.AddProvider(new StreamingLoggerProvider(outputStream, format, level));

            await using var processor = new DiagnosticsEventPipeProcessor(
                PipeMode.Logs,
                loggerFactory: loggerFactory,
                logsLevel: level);

            var client = await GetClientAsync(pid, token);
            await processor.Process(client, pid, duration, token);
        }

        private static NETCore.Client.DumpType MapDumpType(DumpType dumpType)
        {
            switch(dumpType)
            {
                case DumpType.Full:
                    return NETCore.Client.DumpType.Full;
                case DumpType.WithHeap:
                    return NETCore.Client.DumpType.WithHeap;
                case DumpType.Triage:
                    return NETCore.Client.DumpType.Triage;
                case DumpType.Mini:
                    return NETCore.Client.DumpType.Normal;
                default:
                    throw new ArgumentException("Unexpected dumpType", nameof(dumpType));
            }
        }

        public async Task<int> ResolveProcessAsync(int? pid, CancellationToken token)
        {
            if (pid.HasValue)
            {
                return pid.Value;
            }

            // Short-circuit for when running in a Docker container.
            if (RuntimeInfo.IsInDockerContainer)
            {
                try
                {
                    var client = await GetClientAsync(DockerEntrypointProcessId, token);
                    using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                    
                    await client.WaitForConnectionAsync(timeoutSource.Token);

                    return DockerEntrypointProcessId;
                }
                catch
                {
                    // Process ID 1 doesn't exist or didn't advertise in the reverse pipe configuration.
                }
            }

            // Only return a process ID if there is exactly one discoverable process.
            IProcessInfo[] processes = (await GetProcessesAsync(token)).ToArray();
            switch (processes.Length)
            {
                case 0:
                    throw new ArgumentException("Unable to discover a target process.");
                case 1:
                    return processes[0].Pid;
                default:
#if DEBUG
                    Process process = processes.Select(p => Process.GetProcessById(p.Pid)).FirstOrDefault(p => string.Equals(p.ProcessName, "iisexpress", StringComparison.OrdinalIgnoreCase));
                    if (process != null)
                    {
                        return process.Id;
                    }
#endif
                    throw new ArgumentException("Unable to select a single target process because multiple target processes have been discovered.");
            }
        }

        private async Task<DiagnosticsClient> GetClientAsync(int processId, CancellationToken token)
        {
            var connections = await _connectionsSource.GetConnectionsAsync(token);
            var connection = connections.FirstOrDefault(c => c.ProcessId == processId);

            if (null == connection)
            {
                throw new InvalidOperationException($"Diagnostics client for process ID {processId} does not exist.");
            }

            return new DiagnosticsClient(connection.Endpoint);
        }

        public void Dispose()
        {
            _tokenSource.Cancel();
        }

        /// <summary>
        /// We want to make sure we destroy files we finish streaming.
        /// We want to make sure that we stream out files since we compress on the fly; the size cannot be known upfront.
        /// CONSIDER The above implies knowledge of how the file is used by the rest api.
        /// </summary>
        private sealed class AutoDeleteFileStream : FileStream
        {
            public AutoDeleteFileStream(string path) : base(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, FileOptions.DeleteOnClose)
            {
            }

            public override bool CanSeek => false;
        }

        /// <summary>
        /// This helper class allows us to return a stream result to the caller and then later dispose
        /// any underlying data structures associated with the DiagnosticsMonitor once the caller is done
        /// processing the stream.
        /// </summary>
        private sealed class StreamWithCleanup : IStreamWithCleanup
        {
            private readonly DiagnosticsMonitor _monitor;

            public StreamWithCleanup(DiagnosticsMonitor monitor, Stream stream)
            {
                Stream = stream;
                _monitor = monitor;
            }

            public Stream Stream { get; }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await _monitor.CurrentProcessingTask;
                }
                finally
                {
                    await _monitor.DisposeAsync();
                }
            }
        }

        private sealed class ProcessInfo : IProcessInfo
        {
            public ProcessInfo(Guid uid, int pid)
            {
                Pid = pid;
                Uid = uid;
            }

            public int Pid { get; }

            public Guid Uid { get; }
        }
    }
}
