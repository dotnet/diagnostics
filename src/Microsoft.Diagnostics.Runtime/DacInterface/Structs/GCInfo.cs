// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct GCInfo
    {
        public readonly int ServerMode;
        public readonly int GCStructuresValid;
        public readonly int HeapCount;
        public readonly int MaxGeneration;
    }
}