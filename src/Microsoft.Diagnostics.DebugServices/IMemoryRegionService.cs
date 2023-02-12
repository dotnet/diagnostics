using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Enumerate virtual address regions, their protections, and usage.
    /// </summary>
    public interface IMemoryRegionService
    {
        IEnumerable<IMemoryRegion> EnumerateRegions();
    }
}
