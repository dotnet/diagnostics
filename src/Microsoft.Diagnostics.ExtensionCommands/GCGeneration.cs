// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public enum GCGeneration
    {
        NotSet = 0,
        Generation0 = 1,
        Generation1 = 2,
        Generation2 = 3,
        LargeObjectHeap = 4,
        PinnedObjectHeap = 5,
        FrozenObjectHeap = 6
    }
}
