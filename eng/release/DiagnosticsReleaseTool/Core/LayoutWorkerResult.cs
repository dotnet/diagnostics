using System.Collections.Generic;

namespace ReleaseTool.Core
{
    public enum LayoutResultStatus
    {
        Error,
        FileNotHandled,
        FileHandled,
    }

    public struct LayoutWorkerResult
    {
        public LayoutResultStatus Status { get; }
        public IEnumerable<(FileMapping fileMap, FileMetadata fileMetadata)> LayoutDataEnumerable { get; }

        public LayoutWorkerResult(LayoutResultStatus status,
            IEnumerable<(FileMapping fileMap, FileMetadata fileMetadata)> fileLayoutDataEnumerable)
        {
            Status = status;
            LayoutDataEnumerable = Status == LayoutResultStatus.FileHandled && fileLayoutDataEnumerable is not null
                                                ? fileLayoutDataEnumerable
                                                : System.Linq.Enumerable.Empty<(FileMapping, FileMetadata)>();
        }
        public LayoutWorkerResult(LayoutResultStatus status) : this(status, null) {}
    }
}