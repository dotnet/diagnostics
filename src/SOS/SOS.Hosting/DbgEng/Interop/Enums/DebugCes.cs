// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_CES : uint
    {
        ALL = 0xffffffff,
        CURRENT_THREAD = 1,
        EFFECTIVE_PROCESSOR = 2,
        BREAKPOINTS = 4,
        CODE_LEVEL = 8,
        EXECUTION_STATUS = 0x10,
        ENGINE_OPTIONS = 0x20,
        LOG_FILE = 0x40,
        RADIX = 0x80,
        EVENT_FILTERS = 0x100,
        PROCESS_OPTIONS = 0x200,
        EXTENSIONS = 0x400,
        SYSTEMS = 0x800,
        ASSEMBLY_OPTIONS = 0x1000,
        EXPRESSION_SYNTAX = 0x2000,
        TEXT_REPLACEMENTS = 0x4000
    }
}