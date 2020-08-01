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
        // A Docker container's entrypoint process ID is 1
        private static readonly ProcessFilter DockerEntrypointProcessFilter = new ProcessFilter(1);

        // The amount of time to wait when checking if the docker entrypoint process is a .NET process
        // with a diagnostics transport connection.
        private static readonly TimeSpan DockerEntrypointWaitTimeout = TimeSpan.FromMilliseconds(250);

        private readonly IEndpointInfoSourceInternal _endpointInfoSource;
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public DiagnosticServices(IEndpointInfoSource endpointInfoSource)
        {
            _endpointInfoSource = (IEndpointInfoSourceInternal)endpointInfoSource;
        }

        public async Task<IEnumerable<IProcessInfo>> GetProcessesAsync(CancellationToken token)
        {
            try
            {
                var endpointInfos = await _endpointInfoSource.GetEndpointInfoAsync(token);

                return endpointInfos.Select(ProcessInfo.FromEndpointInfo);
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Unable to enumerate processes.");
            }
        }

        public async Task<Stream> GetDump(IProcessInfo pi, DumpType mode, CancellationToken token)
        {
            string dumpFilePath = Path.Combine(Path.GetTempPath(), FormattableString.Invariant($"{Guid.NewGuid()}_{pi.Pid}"));
            NETCore.Client.DumpType dumpType = MapDumpType(mode);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Get the process
                Process process = Process.GetProcessById(pi.Pid);
                await Dumper.CollectDumpAsync(process, dumpFilePath, dumpType);
            }
            else
            {
                await Task.Run(() =>
                {
                    pi.Client.WriteDump(dumpType, dumpFilePath);
                });
            }

            return new AutoDeleteFileStream(dumpFilePath);
        }

        public async Task<Stream> GetGcDump(IProcessInfo pi, CancellationToken token)
        {
            var graph = new MemoryGraph(50_000);
            await using var processor = new DiagnosticsEventPipeProcessor(
                PipeMode.GCDump,
                gcGraph: graph);

            await processor.Process(pi.Client, pi.Pid, Timeout.InfiniteTimeSpan, token);

            var dumper = new GCHeapDump(graph);
            dumper.CreationTool = "dotnet-monitor";

            var stream = new MemoryStream();
            var serializer = new Serializer(stream, dumper, leaveOpen: true);
            serializer.Close();

            stream.Position = 0;
            return stream;
        }

        public async Task<IStreamWithCleanup> StartTrace(IProcessInfo pi, MonitoringSourceConfiguration configuration, TimeSpan duration, CancellationToken token)
        {
            DiagnosticsMonitor monitor = new DiagnosticsMonitor(configuration);
            Stream stream = await monitor.ProcessEvents(pi.Client, duration, token);
            return new StreamWithCleanup(monitor, stream);
        }

        public async Task StartLogs(Stream outputStream, IProcessInfo pi, TimeSpan duration, LogFormat format, LogLevel level, CancellationToken token)
        {
            using var loggerFactory = new LoggerFactory();

            loggerFactory.AddProvider(new StreamingLoggerProvider(outputStream, format, level));

            await using var processor = new DiagnosticsEventPipeProcessor(
                PipeMode.Logs,
                loggerFactory: loggerFactory,
                logsLevel: level);

            await processor.Process(pi.Client, pi.Pid, duration, token);
        }

        private static NETCore.Client.DumpType MapDumpType(DumpType dumpType)
        {
            switch (dumpType)
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

        public async Task<IProcessInfo> GetProcessAsync(ProcessFilter? filter, CancellationToken token)
        {
            var endpointInfos = await _endpointInfoSource.GetEndpointInfoAsync(token);

            if (filter.HasValue)
            {
                return GetSingleProcessInfo(
                    endpointInfos,
                    filter);
            }

            // Short-circuit for when running in a Docker container.
            if (RuntimeInfo.IsInDockerContainer)
            {
                try
                {
                    IProcessInfo processInfo = GetSingleProcessInfo(
                        endpointInfos,
                        DockerEntrypointProcessFilter);

                    using var timeoutSource = new CancellationTokenSource(DockerEntrypointWaitTimeout);

                    await processInfo.Client.WaitForConnectionAsync(timeoutSource.Token);

                    return processInfo;
                }
                catch
                {
                    // Process ID 1 doesn't exist, didn't advertise in connect mode, or is not a .NET process.
                }
            }

            return GetSingleProcessInfo(
                endpointInfos,
                filter: null);
        }

        private IProcessInfo GetSingleProcessInfo(IEnumerable<IEndpointInfo> endpointInfos, ProcessFilter? filter)
        {
            if (filter.HasValue)
            {
                if (filter.Value.RuntimeInstanceCookie.HasValue)
                {
                    Guid cookie = filter.Value.RuntimeInstanceCookie.Value;
                    endpointInfos = endpointInfos.Where(info => info.RuntimeInstanceCookie == cookie);
                }

                if (filter.Value.ProcessId.HasValue)
                {
                    int pid = filter.Value.ProcessId.Value;
                    endpointInfos = endpointInfos.Where(info => info.ProcessId == pid);
                }
            }

            IEndpointInfo[] endpointInfoArray = endpointInfos.ToArray();
            switch (endpointInfoArray.Length)
            {
                case 0:
                    throw new ArgumentException("Unable to discover a target process.");
                case 1:
                    return ProcessInfo.FromEndpointInfo(endpointInfoArray[0]);
                default:
#if DEBUG
                    IEndpointInfo endpointInfo = endpointInfoArray.FirstOrDefault(info => string.Equals(Process.GetProcessById(info.ProcessId).ProcessName, "iisexpress", StringComparison.OrdinalIgnoreCase));
                    if (endpointInfo != null)
                    {
                        return ProcessInfo.FromEndpointInfo(endpointInfo);
                    }
#endif
                    throw new ArgumentException("Unable to select a single target process because multiple target processes have been discovered.");
            }
        }

        private async Task<DiagnosticsClient> GetClientAsync(int processId, CancellationToken token)
        {
            var endpointInfos = await _endpointInfoSource.GetEndpointInfoAsync(token);
            var endpointInfo = endpointInfos.FirstOrDefault(c => c.ProcessId == processId);

            if (null == endpointInfo)
            {
                throw new InvalidOperationException($"Diagnostics client for process ID {processId} does not exist.");
            }

            return new DiagnosticsClient(endpointInfo.Endpoint);
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
            public ProcessInfo(DiagnosticsClient client, Guid uid, int pid)
            {
                Client = client;
                Pid = pid;
                Uid = uid;
            }

            public static ProcessInfo FromEndpointInfo(IEndpointInfo endpointInfo)
            {
                if (null == endpointInfo)
                {
                    throw new ArgumentNullException(nameof(endpointInfo));
                }

                return new ProcessInfo(
                    new DiagnosticsClient(endpointInfo.Endpoint),
                    endpointInfo.RuntimeInstanceCookie,
                    endpointInfo.ProcessId);
            }

            public DiagnosticsClient Client { get; }

            public int Pid { get; }

            public Guid Uid { get; }
        }
    }
}
