// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_OUTTYPE
    {
        DEFAULT = 0,
        NO_INDENT = 1,
        NO_OFFSET = 2,
        VERBOSE = 4,
        COMPACT_OUTPUT = 8,
        ADDRESS_OF_FIELD = 0x10000,
        ADDRESS_ANT_END = 0x20000,
        BLOCK_RECURSE = 0x200000
    }
}
