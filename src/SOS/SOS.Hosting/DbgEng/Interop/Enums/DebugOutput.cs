// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_OUTPUT : uint
    {
        NORMAL = 1,
        ERROR = 2,
        WARNING = 4,
        VERBOSE = 8,
        PROMPT = 0x10,
        PROMPT_REGISTERS = 0x20,
        EXTENSION_WARNING = 0x40,
        DEBUGGEE = 0x80,
        DEBUGGEE_PROMPT = 0x100,
        SYMBOLS = 0x200
    }
}
