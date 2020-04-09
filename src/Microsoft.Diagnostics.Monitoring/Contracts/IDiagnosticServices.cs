using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// Set of services provided by the monitoring tool. These are consumed by
    /// the REST Api.
    /// </summary>
    public interface IDiagnosticServices : IDisposable
    {
        IEnumerable<int> GetProcesses();

        Task<Stream> GetDump(int pid, DumpType mode);

        //TODO We can most likely unify trace, cpu, and logs/metrics around one call with the appropriate config
        Task<Stream> StartCpuTrace(int pid, int duration, CancellationToken token);
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
        public string Configuraton { get; set; }
    }
}
