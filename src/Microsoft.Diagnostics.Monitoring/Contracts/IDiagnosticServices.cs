using System;
using System.Collections.Generic;
using System.IO;
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

        Task<OperationResult<Stream>> GetDump(int? pid, DumpType mode);

        //TODO We can most likely unify trace, cpu, and logs/metrics around one call with the appropriate config
        Task<OperationResult<Stream>> StartCpuTrace(int? pid, int duration, CancellationToken token);
    }

    public enum DumpType
    {
        Full = 1,
        Mini,
        WithHeap,
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

    public sealed class OperationResult<T>
    {
        public int Pid { get; set; }

        public T Value { get; set; }
    }
}
