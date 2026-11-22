// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    public enum ServiceScope
    {
        Global,
        Context,
        Target,
        Module,
        Thread,
        Runtime,
        Max
    }
}
