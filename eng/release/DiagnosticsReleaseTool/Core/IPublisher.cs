using System;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public interface IPublisher : IDisposable
    {
        Task<string> PublishFileAsync(FileMapping fileData, CancellationToken ct);
    }
}