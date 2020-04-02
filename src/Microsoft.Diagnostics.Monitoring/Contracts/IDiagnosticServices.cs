using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// Set of services provided by the monitoring tool. These are consumed by
    /// the REST Api.
    /// </summary>
    public interface IDiagnosticServices : IDisposable
    {
        public IEnumerable<int> GetProcesses();

        public Task<Stream> GetDump(int pid, DumpType mode);

        public Task<Stream> StartNetTrace(int pid, TraceRequest traceRequest);

        public Task StopNetTrace(int pid, TraceRequest traceRequest);
    }

    public enum DumpType
    {
        Full = 1,
        Normal,
        MiniWithHeap,
        Triage
    }

    public enum TraceState
    {
        Stopped = 0,
        Running = 1,
    }

    public sealed class TraceRequest
    {
        public TraceState State { get; set; }

        public string Configuraton { get; set; }
    }
}
