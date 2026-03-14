// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum MODULE_ORDERS : uint
    {
        MASK = 0xF0000000,
        LOADTIME = 0x10000000,
        MODULENAME = 0x20000000
    }
}
