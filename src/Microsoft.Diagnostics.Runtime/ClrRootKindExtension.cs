// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    internal static class ClrRootKindExtension
    {
        public static string GetName(this ClrRootKind kind) => kind switch
        {
            ClrRootKind.None => "none",
            ClrRootKind.FinalizerQueue => "finalizer root",
            ClrRootKind.StrongHandle => "strong handle",
            ClrRootKind.PinnedHandle => "pinned handle",
            ClrRootKind.Stack => "stack root",
            ClrRootKind.RefCountedHandle => "ref counted handle",
            ClrRootKind.AsyncPinnedHandle => "async pinned handle",
            ClrRootKind.SizedRefHandle => "sized ref handle",
            _ => "unknown root"
        };
    }
}
