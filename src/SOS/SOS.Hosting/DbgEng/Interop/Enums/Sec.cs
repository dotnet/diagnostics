// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum SEC : uint
    {
        FILE = 0x800000,
        IMAGE = 0x1000000,
        PROTECTED_IMAGE = 0x2000000,
        RESERVE = 0x4000000,
        COMMIT = 0x8000000,
        NOCACHE = 0x10000000,
        WRITECOMBINE = 0x40000000,
        LARGE_PAGES = 0x80000000,
        MEM_IMAGE = IMAGE
    }
}
