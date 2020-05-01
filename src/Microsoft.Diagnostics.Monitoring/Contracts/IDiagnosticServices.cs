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

        int ResolveProcess(int? pid);

        Task<Stream> GetDump(int pid, DumpType mode);

        Task<Stream> GetGcDump(int pid, TimeSpan timeout, CancellationToken token);

        //TODO We can most likely unify trace, cpu, and logs/metrics around one call with the appropriate config
        Task<IStreamWithCleanup> StartCpuTrace(int pid, int duration, CancellationToken token);

        Task<IStreamWithCleanup> StartTrace(int pid, int duration, CancellationToken token);

        Task StartLogs(Stream outputStream, int pid, int duration, CancellationToken token);
    }

    public interface IStreamWithCleanup : IAsyncDisposable
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
}
