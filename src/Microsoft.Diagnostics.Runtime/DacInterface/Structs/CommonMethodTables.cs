// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct CommonMethodTables
    {
        public readonly ClrDataAddress ArrayMethodTable;
        public readonly ClrDataAddress StringMethodTable;
        public readonly ClrDataAddress ObjectMethodTable;
        public readonly ClrDataAddress ExceptionMethodTable;
        public readonly ClrDataAddress FreeMethodTable;
    }
}