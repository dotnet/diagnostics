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
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class DiagnosticServices : IDiagnosticServices
    {
        private readonly Dictionary<int, EventPipeSession> _sessions = new Dictionary<int, EventPipeSession>();
        private readonly ILogger<DiagnosticsMonitor> _logger;

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

        public Task<Stream> StartNetTrace(int pid, TraceRequest traceRequest)
        {
            if (_sessions.ContainsKey(pid))
            {
                throw new InvalidOperationException("Trace has already started");
            }

            var client = new DiagnosticsClient(pid);

            //TODO Pull event providers from the configuration.
            var cpuProviders = new EventPipeProvider[] {
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", System.Diagnostics.Tracing.EventLevel.Informational),
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", System.Diagnostics.Tracing.EventLevel.Informational, (long)Tracing.Parsers.ClrTraceEventParser.Keywords.Default)
                };

            EventPipeSession session = client.StartEventPipeSession(cpuProviders, requestRundown: true);

            _sessions.Add(pid, session);
            return Task.FromResult(session.EventStream);
        }

        public Task StopNetTrace(int pid, TraceRequest traceRequest)
        {
            if (!_sessions.TryGetValue(pid, out EventPipeSession session))
            {
                throw new InvalidOperationException("No running trace session");

            }
            _sessions.Remove(pid);
            session.Stop();

            return Task.CompletedTask;
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
            if (_sessions != null)
            {
                foreach(KeyValuePair<int, EventPipeSession> session in _sessions)
                {
                    //CONSIDER Should we also stop the sessions here?
                    session.Value?.Dispose();
                }
                _sessions.Clear();
            }
        }
    }
}
