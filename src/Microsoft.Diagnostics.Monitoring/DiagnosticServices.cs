using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class DiagnosticServices : IDiagnosticServices
    {
        private const int MaxTraceSeconds = 60 * 5;

        private readonly ILogger<DiagnosticsMonitor> _logger;
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public DiagnosticServices(ILogger<DiagnosticsMonitor> logger)
        {
            _logger = logger;
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
            if (!pid.HasValue)
            {
                pid = GetSingleProcessId();
            }

            string dumpFilePath = FormattableString.Invariant($@"{Path.GetTempPath()}{Path.DirectorySeparatorChar}{Guid.NewGuid()}_{pid.Value}");
            NETCore.Client.DumpType dumpType = MapDumpType(mode);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Get the process
                Process process = Process.GetProcessById(pid.Value);
                await Dumper.CollectDumpAsync(process, dumpFilePath, dumpType);
            }
            else
            {
                await Task.Run(() =>
                {
                    var client = new DiagnosticsClient(pid.Value);
                    client.WriteDump(dumpType, dumpFilePath);
                });
            }

            return CreateResult(pid.Value, new AutoDeleteFileStream(dumpFilePath));
        }

        public Task<OperationResult<Stream>> StartCpuTrace(int? pid, int durationSeconds, CancellationToken cancellationToken)
        {
            if ((durationSeconds < 1) || (durationSeconds > MaxTraceSeconds))
            {
                throw new InvalidOperationException("Invalid duration");
            }

            if (!pid.HasValue)
            {
                pid = GetSingleProcessId();
            }

            //TODO Should we limit only 1 trace per file?
            var client = new DiagnosticsClient(pid.Value);

            //TODO Pull event providers from the configuration.
            var cpuProviders = new EventPipeProvider[] {
                new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", System.Diagnostics.Tracing.EventLevel.Informational),
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", System.Diagnostics.Tracing.EventLevel.Informational, (long)Tracing.Parsers.ClrTraceEventParser.Keywords.Default)
            };

            EventPipeSession session = client.StartEventPipeSession(cpuProviders, requestRundown: true);

            CancellationTokenSource linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_tokenSource.Token, cancellationToken);
            Task traceTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(durationSeconds), linkedTokenSource.Token);
                }
                finally
                {
                    session.Stop();
                    linkedTokenSource.Dispose();
                    //We rely on the caller to Dispose the EventStream file.
                }
            }, CancellationToken.None);

            return Task.FromResult(CreateResult(pid.Value, session.EventStream));
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

        private int GetSingleProcessId()
        {
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

        private static OperationResult<Stream> CreateResult(int pid, Stream stream)
        {
            return new OperationResult<Stream>()
            {
                Pid = pid,
                Value = stream
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
    }
}
