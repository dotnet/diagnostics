using Microsoft.Diagnostics.Monitoring.RestServer;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            //TODO This won't work properly with multi-container scenarios that don't share the process space.
            //TODO We will need to use DiagnosticsAgent if we are the server.
            return DiagnosticsClient.GetPublishedProcesses();
        }

        public async Task<Stream> GetDump(int pid, DumpType mode)
        {
            string dumpFilePath = FormattableString.Invariant($@"{Path.GetTempPath()}{Path.DirectorySeparatorChar}{Guid.NewGuid()}_{pid}");
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

            return new FileStreamWrapper(dumpFilePath);
        }

        public Task<Stream> StartCpuTrace(int pid, int durationSeconds)
        {
            if ((durationSeconds < 1) || (durationSeconds > MaxTraceSeconds))
            {
                throw new InvalidOperationException("Invalid duration");
            }

            //TODO Should we limit only 1 trace per file?
            var client = new DiagnosticsClient(pid);

            //TODO Pull event providers from the configuration.
            var cpuProviders = new EventPipeProvider[] {
                new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", System.Diagnostics.Tracing.EventLevel.Informational),
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", System.Diagnostics.Tracing.EventLevel.Informational, (long)Tracing.Parsers.ClrTraceEventParser.Keywords.Default)
            };

            EventPipeSession session = client.StartEventPipeSession(cpuProviders, requestRundown: true);

            CancellationToken token = _tokenSource.Token;
            Task traceTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(durationSeconds), token);
                }
                finally
                {
                    session.Stop();
                    //We rely on the caller to Dispose the EventStream file.
                }
            });

            return Task.FromResult(session.EventStream);

        }

        private static NETCore.Client.DumpType MapDumpType(DumpType dumpType)
        {
            switch(dumpType)
            {
                case DumpType.Full:
                    return NETCore.Client.DumpType.Full;
                case DumpType.MiniWithHeap:
                    return NETCore.Client.DumpType.WithHeap;
                case DumpType.Triage:
                    return NETCore.Client.DumpType.Triage;
                case DumpType.Normal:
                    return NETCore.Client.DumpType.Normal;
                default:
                    throw new ArgumentException("Unexpected dumpType", nameof(dumpType));
            }
        }

        public void Dispose()
        {
            _tokenSource.Cancel();
            _tokenSource.Dispose();
        }
    }
}
