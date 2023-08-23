// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A legacy ThreadPool work request item.  This is only relevant for DesktopCLR.
    /// </summary>
    public struct LegacyThreadPoolWorkRequest
    {
        public bool IsAsyncTimerCallback { get; internal set; }
        public ulong Context { get; internal set; }
        public ulong Function { get; internal set; }
    }
}