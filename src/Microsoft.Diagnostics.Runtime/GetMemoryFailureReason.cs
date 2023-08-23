// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    public enum GetMemoryFailureReason
    {
        None = 0,
        ReserveSegment = 1,
        CommitSegmentBegin = 2,
        CommitEphemeralSegment = 3,
        GrowTable = 4,
        CommitTable = 5
    }
}
