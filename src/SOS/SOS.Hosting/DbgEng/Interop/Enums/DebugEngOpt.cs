﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_ENGOPT : uint
    {
        NONE = 0,
        IGNORE_DBGHELP_VERSION = 0x00000001,
        IGNORE_EXTENSION_VERSIONS = 0x00000002,
        ALLOW_NETWORK_PATHS = 0x00000004,
        DISALLOW_NETWORK_PATHS = 0x00000008,
        NETWORK_PATHS = 0x00000004 | 0x00000008,
        IGNORE_LOADER_EXCEPTIONS = 0x00000010,
        INITIAL_BREAK = 0x00000020,
        INITIAL_MODULE_BREAK = 0x00000040,
        FINAL_BREAK = 0x00000080,
        NO_EXECUTE_REPEAT = 0x00000100,
        FAIL_INCOMPLETE_INFORMATION = 0x00000200,
        ALLOW_READ_ONLY_BREAKPOINTS = 0x00000400,
        SYNCHRONIZE_BREAKPOINTS = 0x00000800,
        DISALLOW_SHELL_COMMANDS = 0x00001000,
        KD_QUIET_MODE = 0x00002000,
        DISABLE_MANAGED_SUPPORT = 0x00004000,
        DISABLE_MODULE_SYMBOL_LOAD = 0x00008000,
        DISABLE_EXECUTION_COMMANDS = 0x00010000,
        DISALLOW_IMAGE_FILE_MAPPING = 0x00020000,
        PREFER_DML = 0x00040000,
        ALL = 0x0007FFFF
    }
}