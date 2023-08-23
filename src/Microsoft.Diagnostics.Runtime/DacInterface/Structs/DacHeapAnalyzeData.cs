// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct DacHeapAnalyzeData
    {
        public readonly ClrDataAddress HeapAddress;
        public readonly ClrDataAddress InternalRootArray;
        public readonly ulong InternalRootArrayIndex;
        public readonly bool HeapAnalyzeSuccess;
    }
}
