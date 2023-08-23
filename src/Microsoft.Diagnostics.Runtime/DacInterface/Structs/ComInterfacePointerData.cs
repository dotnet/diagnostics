// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct COMInterfacePointerData
    {
        public readonly ClrDataAddress MethodTable;
        public readonly ClrDataAddress InterfacePointer;
        public readonly ClrDataAddress ComContext;
    }
}
