// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.DbgEng
{
    [Flags]
    internal enum MEM : uint
    {
        COMMIT = 0x1000,
        RESERVE = 0x2000,
        DECOMMIT = 0x4000,
        RELEASE = 0x8000,
        FREE = 0x10000,
        PRIVATE = 0x20000,
        MAPPED = 0x40000,
        RESET = 0x80000,
        TOP_DOWN = 0x100000,
        WRITE_WATCH = 0x200000,
        PHYSICAL = 0x400000,
        ROTATE = 0x800000,
        LARGE_PAGES = 0x20000000,
        FOURMB_PAGES = 0x80000000,

        IMAGE = 0x1000000
    }
}
