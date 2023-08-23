// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal interface IClrNativeHeapHelpers
    {
        IEnumerable<ClrNativeHeapInfo> EnumerateNativeHeaps(ClrAppDomain domain);
        IEnumerable<ClrNativeHeapInfo> EnumerateNativeHeaps(ClrJitManager jitManager);
        IEnumerable<ClrNativeHeapInfo> EnumerateLoaderAllocatorNativeHeaps(ulong loaderAllocator);
        IEnumerable<ClrNativeHeapInfo> EnumerateThunkHeaps(ulong thunkHeapAddress);
    }
}