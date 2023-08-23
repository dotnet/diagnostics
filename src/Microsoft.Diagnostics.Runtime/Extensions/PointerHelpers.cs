// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    internal static class PointerHelpers
    {
        public static IntPtr AsIntPtr(this ulong address) => new((nint)address);
    }
}
