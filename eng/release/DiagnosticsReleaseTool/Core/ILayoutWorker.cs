using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public interface ILayoutWorker : IDisposable
    {
        ValueTask<LayoutWorkerResult> HandleFileAsync(FileInfo file, CancellationToken ct);
    }
}