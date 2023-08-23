// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    [Flags]
    public enum SyncBlockComFlags : byte
    {
        None = 0,
        ComCallableWrapper = 1,
        RuntimeCallableWrapper = 2,
        ComClassFactory = 4
    }
}
