// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct _DEBUG_TYPED_DATA
    {
        public ulong ModBase;
        public ulong Offset;
        public ulong EngineHandle;
        public ulong Data;
        public uint Size;
        public uint Flags;
        public uint TypeId;
        public uint BaseTypeId;
        public uint Tag;
        public uint Register;
        public fixed ulong Internal[9];
    }
}