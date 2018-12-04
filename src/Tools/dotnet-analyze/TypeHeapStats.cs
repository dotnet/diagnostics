using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.Tools.Analyze
{
    public class TypeHeapStats
    {
        public ClrType Type { get; }
        public long Count { get; private set; }
        public ulong TotalSize { get; private set; }

        public TypeHeapStats(ClrType type)
        {
            Type = type;
        }

        public void AddObject(ulong size)
        {
            Count++;
            TotalSize += size;
        }
    }
}
