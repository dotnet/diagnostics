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
using Microsoft.Diagnostics.Monitoring.Contracts;
using Microsoft.Diagnostics.Monitoring.Logging;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class DiagnosticServices : IDiagnosticServices
    {
        private const int MaxTraceSeconds = 60 * 5;

        private readonly ILogger<DiagnosticsMonitor> _logger;
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private IEnumerable<IMetricsLogger> _metricsLoggers;
        private ContextConfiguration _contextConfiguration;
        private IServiceProvider _loggerServiceProvider;

        public DiagnosticServices(ILogger<DiagnosticsMonitor> logger,
            IOptions<ContextConfiguration> contextConfig,
            IEnumerable<IMetricsLogger> metricsLoggers,
            IStreamAccessor streamingAccessor)
        {
            _logger = logger;
            _metricsLoggers = metricsLoggers;
            _contextConfiguration = contextConfig.Value;

            //We want to use ILogger since at some point in the future we may want to push this data to other
            //sinks. At the same time we want these to be transient for the purposes of streaming the data, but not for other logs (such as those of
            //the rest service itself).
            ServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(streamingAccessor);
            serviceCollection.AddLogging(builder => builder.Services.AddSingleton<ILoggerProvider, StreamingLoggerProvider>());
            _loggerServiceProvider = serviceCollection.BuildServiceProvider();
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

        public async Task<OperationResult<Stream>> GetDump(int? pid, DumpType mode)
        {
            int pidValue = GetSingleProcessId(pid);

            string dumpFilePath = FormattableString.Invariant($@"{Path.GetTempPath()}{Path.DirectorySeparatorChar}{Guid.NewGuid()}_{pidValue}");
            NETCore.Client.DumpType dumpType = MapDumpType(mode);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Get the process
                Process process = Process.GetProcessById(pidValue);
                await Dumper.CollectDumpAsync(process, dumpFilePath, dumpType);
            }
            else
            {
                await Task.Run(() =>
                {
                    var client = new DiagnosticsClient(pidValue);
                    client.WriteDump(dumpType, dumpFilePath);
                });
            }

            return CreateResult<Stream>(pidValue, new AutoDeleteFileStream(dumpFilePath));
        }

        public async Task<OperationResult<IStreamResult>> StartCpuTrace(int? pid, int durationSeconds, CancellationToken cancellationToken)
        {
            if ((durationSeconds < 1) || (durationSeconds > MaxTraceSeconds))
            {
                throw new InvalidOperationException("Invalid duration");
            }

            int pidValue = GetSingleProcessId(pid);

            DiagnosticsMonitor monitor = new DiagnosticsMonitor(new CpuProfileConfiguration());
            Stream stream = await monitor.ProcessEvents(pidValue, durationSeconds, cancellationToken);

            return CreateResult<IStreamResult>(pidValue, new StreamResult(monitor, stream));
        }

        public async Task<OperationResult<IStreamResult>> StartTrace(int? pid, int durationSeconds, CancellationToken token)
        {
            if ((durationSeconds < 1) || (durationSeconds > MaxTraceSeconds))
            {
                throw new InvalidOperationException("Invalid duration");
            }

            int pidValue = GetSingleProcessId(pid);

            DiagnosticsMonitor monitor = new DiagnosticsMonitor(new LoggingSourceConfiguration());
            Stream stream = await monitor.ProcessEvents(pidValue, durationSeconds, token);
            return CreateResult<IStreamResult>(pidValue, new StreamResult(monitor, stream));
        }

        public async Task<OperationResult> StartLogs(Stream outputStream, int? pid, int durationSeconds, CancellationToken token)
        {
            if ((durationSeconds < 1) || (durationSeconds > MaxTraceSeconds))
            {
                throw new InvalidOperationException("Invalid duration");
            }

            int pidValue = GetSingleProcessId(pid);



            var processor = new DiagnosticsEventPipeProcessor(_contextConfiguration, 
                PipeMode.Logs,
                _loggerServiceProvider.GetRequiredService<ILoggerFactory>(),
                Enumerable.Empty<IMetricsLogger>());

            try
            {
                await processor.Process(pidValue, durationSeconds, token);
            }
            finally
            {
                await processor.DisposeAsync();
            }

            return new OperationResult { Pid = pidValue };
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

        private int GetSingleProcessId(int? pid)
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

        private static OperationResult<T> CreateResult<T>(int pid, T value)
        {
            return new OperationResult<T>()
            {
                Pid = pid,
                Value = value
            };
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
        private sealed class StreamResult : IStreamResult
        {
            private readonly DiagnosticsMonitor _monitor;

            public StreamResult(DiagnosticsMonitor monitor, Stream stream)
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
