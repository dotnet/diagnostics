// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    // The shared projection takes opaque pointers for domain- and thread-local module data.
    [StructLayout(LayoutKind.Explicit, Size = 0x30)]
    internal struct DacpLocalModuleData
    {
        [FieldOffset(0x10)] public ulong ClassData;
        [FieldOffset(0x20)] public ulong GcStaticDataStart;
        [FieldOffset(0x28)] public ulong NonGcStaticDataStart;
    }
}
