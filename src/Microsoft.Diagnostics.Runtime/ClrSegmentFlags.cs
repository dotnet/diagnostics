// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    public enum ClrSegmentFlags : ulong
    {
        None = 0,
        ReadOnly = 1,
        Swept = 16,
    }
}