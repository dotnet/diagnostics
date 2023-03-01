﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
