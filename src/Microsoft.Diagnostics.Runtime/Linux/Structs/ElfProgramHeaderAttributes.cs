// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [Flags]
    internal enum ElfProgramHeaderAttributes : uint
    {
        Executable = 1,             // PF_X
        Writable = 2,               // PF_W
        Readable = 4,               // PF_R
        OSMask = 0x0FF00000,        // PF_MASKOS
        ProcessorMask = 0xF0000000, // PF_MASKPROC
    }
}
