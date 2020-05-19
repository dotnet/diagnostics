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
using Microsoft.Diagnostics.Monitoring.Logging;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class DiagnosticServices : IDiagnosticServices
    {
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private ContextConfiguration _contextConfiguration;

        public DiagnosticServices(IOptions<ContextConfiguration> contextConfig)
        {
            _contextConfiguration = contextConfig.Value;
        }

        public IEnumerable<int> GetProcesses()
        {
            try
            {
                //TODO This won't work properly with multi-container scenarios that don't share the process space.
                //TODO We will need to use DiagnosticsAgent if we are the server.
                return DiagnosticsClient.GetPublishedProcesses();
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Unable to enumerate processes.");
            }
        }

        public async Task<Stream> GetDump(int pid, DumpType mode)
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
                await Task.Run(() =>
                {
                    var client = new DiagnosticsClient(pid);
                    client.WriteDump(dumpType, dumpFilePath);
                });
            }

            return new AutoDeleteFileStream(dumpFilePath);
        }

        public async Task<Stream> GetGcDump(int pid, CancellationToken cancellationToken)
        {
            var graph = new MemoryGraph(50_000);
            await using var processor = new DiagnosticsEventPipeProcessor(_contextConfiguration,
                PipeMode.GCDump,
                gcGraph: graph);

            await processor.Process(pid, Timeout.InfiniteTimeSpan, cancellationToken);

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
            Stream stream = await monitor.ProcessEvents(pid, duration, token);
            return new StreamWithCleanup(monitor, stream);
        }

        public async Task StartLogs(Stream outputStream, int pid, TimeSpan duration, CancellationToken token)
        {
            using var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new StreamingLoggerProvider(outputStream));

            await using var processor = new DiagnosticsEventPipeProcessor(_contextConfiguration,
                PipeMode.Logs,
                loggerFactory: loggerFactory);

            await processor.Process(pid, duration, token);
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

        public int ResolveProcess(int? pid)
        {
            if (pid.HasValue)
            {
                return pid.Value;
            }

            // Short-circuit for when running in a Docker container, assuming the entrypoint
            // of the container is a dotnet application.
            if (RuntimeInfo.IsInDockerContainer && null != Process.GetProcessById(1))
            {
                return 1;
            }

            // Only return a process ID if there is exactly one discoverable process.
            int[] pids = GetProcesses().ToArray();
            switch (pids.Length)
            {
                case 0:
                    throw new ArgumentException("Unable to discover a target process.");
                case 1:
                    return pids[0];
                default:
                    throw new ArgumentException("Unable to select a single target process because multiple target processes have been discovered.");
            }
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
    }
}
