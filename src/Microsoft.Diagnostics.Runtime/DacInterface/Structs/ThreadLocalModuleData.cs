// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ThreadLocalModuleData
    {
        public readonly ClrDataAddress ThreadAddress;
        public readonly ClrDataAddress ModuleIndex;

        public readonly ClrDataAddress ClassData;
        public readonly ClrDataAddress DynamicClassTable;
        public readonly ClrDataAddress GCStaticDataStart;
        public readonly ClrDataAddress NonGCStaticDataStart;
    }
}
