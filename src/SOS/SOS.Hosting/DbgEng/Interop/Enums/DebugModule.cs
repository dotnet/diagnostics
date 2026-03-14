// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_MODULE : uint
    {
        LOADED = 0,
        UNLOADED = 1,
        USER_MODE = 2,
        EXE_MODULE = 4,
        EXPLICIT = 8,
        SECONDARY = 0x10,
        SYNTHETIC = 0x20,
        SYM_BAD_CHECKSUM = 0x10000
    }
}
