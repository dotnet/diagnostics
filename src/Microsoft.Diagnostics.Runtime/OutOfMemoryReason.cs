// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    public enum OutOfMemoryReason
    {
        None = 0,
        Budget = 1,
        CantCommit = 2,
        CantReserve = 3,
        LOH = 4,
        LowMem = 5,
        UnproductiveFullGC = 6
    }
}