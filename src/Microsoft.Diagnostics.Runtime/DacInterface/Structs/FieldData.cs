// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct FieldData
    {
        public readonly uint ElementType; // CorElementType
        public readonly uint SigType; // CorElementType
        public readonly ClrDataAddress TypeMethodTable; // NULL if Type is not loaded
        public readonly ClrDataAddress TypeModule;
        public readonly uint TypeToken;
        public readonly uint FieldToken;
        public readonly ClrDataAddress MTOfEnclosingClass;
        public readonly uint Offset;
        public readonly uint IsThreadLocal;
        public readonly uint IsContextLocal;
        public readonly uint IsStatic;
        public readonly ClrDataAddress NextField;
    }
}
