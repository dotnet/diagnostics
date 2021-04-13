using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    internal class SkipLayoutWorker : ILayoutWorker
    {
        private readonly Func<FileInfo, bool> _shouldHandleFileFunc;

        public SkipLayoutWorker(Func<FileInfo, bool> shouldHandleFileFunc)
        {
            _shouldHandleFileFunc = shouldHandleFileFunc;
        }

        public void Dispose() {}

        public ValueTask<LayoutWorkerResult> HandleFileAsync(FileInfo file, CancellationToken ct)
        {
            LayoutResultStatus status = _shouldHandleFileFunc(file) ? 
                LayoutResultStatus.FileHandled : LayoutResultStatus.FileNotHandled;

            return new ValueTask<LayoutWorkerResult>(new LayoutWorkerResult(status));
        }
    }
}