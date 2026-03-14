// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_CURRENT : uint
    {
        DEFAULT = 0xf,
        SYMBOL = 1,
        DISASM = 2,
        REGISTERS = 4,
        SOURCE_LINE = 8
    }
}
