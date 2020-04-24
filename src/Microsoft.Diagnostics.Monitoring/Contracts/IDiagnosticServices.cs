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
        Task<OperationResult<IStreamResult>> StartCpuTrace(int? pid, int duration, CancellationToken token);

        Task<OperationResult<IStreamResult>> StartTrace(int? pid, int duration, CancellationToken token);

        Task<OperationResult> StartLogs(Stream outputStream, int? pid, int duration, CancellationToken token);
    }

    public interface IStreamResult : IAsyncDisposable
    {
        Stream Stream { get; }
    }

    public enum DumpType
    {
        Full = 1,
        Mini,
        WithHeap,
        Triage
    }

    public sealed class TraceRequest
    {
        public string Configuraton { get; set; }
    }

    public class OperationResult
    {
        public int Pid { get; set; }
    }

    public sealed class OperationResult<T> : OperationResult
    {
        public T Value { get; set; }
    }
}
