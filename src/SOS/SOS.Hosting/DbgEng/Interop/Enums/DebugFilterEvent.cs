// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_FILTER_EVENT : uint
    {
        CREATE_THREAD = 0x00000000,
        EXIT_THREAD = 0x00000001,
        CREATE_PROCESS = 0x00000002,
        EXIT_PROCESS = 0x00000003,
        LOAD_MODULE = 0x00000004,
        UNLOAD_MODULE = 0x00000005,
        SYSTEM_ERROR = 0x00000006,
        INITIAL_BREAKPOINT = 0x00000007,
        INITIAL_MODULE_LOAD = 0x00000008,
        DEBUGGEE_OUTPUT = 0x00000009
    }
}
