// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MethodTableData
    {
        public readonly uint IsFree; // everything else is NULL if this is true.
        public readonly ClrDataAddress Module;
        public readonly ClrDataAddress EEClass;
        public readonly ClrDataAddress ParentMethodTable;
        public readonly ushort NumInterfaces;
        public readonly ushort NumMethods;
        public readonly ushort NumVtableSlots;
        public readonly ushort NumVirtuals;
        public readonly uint BaseSize;
        public readonly uint ComponentSize;
        public readonly uint Token;
        public readonly uint AttrClass;
        public readonly uint Shared; // flags & enum_flag_DomainNeutral
        public readonly uint Dynamic;
        public readonly uint ContainsPointers;
    }
}