// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum IG : ushort
    {
        KD_CONTEXT = 1,
        READ_CONTROL_SPACE = 2,
        WRITE_CONTROL_SPACE = 3,
        READ_IO_SPACE = 4,
        WRITE_IO_SPACE = 5,
        READ_PHYSICAL = 6,
        WRITE_PHYSICAL = 7,
        READ_IO_SPACE_EX = 8,
        WRITE_IO_SPACE_EX = 9,
        KSTACK_HELP = 10, // obsolete
        SET_THREAD = 11,
        READ_MSR = 12,
        WRITE_MSR = 13,
        GET_DEBUGGER_DATA = 14,
        GET_KERNEL_VERSION = 15,
        RELOAD_SYMBOLS = 16,
        GET_SET_SYMPATH = 17,
        GET_EXCEPTION_RECORD = 18,
        IS_PTR64 = 19,
        GET_BUS_DATA = 20,
        SET_BUS_DATA = 21,
        DUMP_SYMBOL_INFO = 22,
        LOWMEM_CHECK = 23,
        SEARCH_MEMORY = 24,
        GET_CURRENT_THREAD = 25,
        GET_CURRENT_PROCESS = 26,
        GET_TYPE_SIZE = 27,
        GET_CURRENT_PROCESS_HANDLE = 28,
        GET_INPUT_LINE = 29,
        GET_EXPRESSION_EX = 30,
        TRANSLATE_VIRTUAL_TO_PHYSICAL = 31,
        GET_CACHE_SIZE = 32,
        READ_PHYSICAL_WITH_FLAGS = 33,
        WRITE_PHYSICAL_WITH_FLAGS = 34,
        POINTER_SEARCH_PHYSICAL = 35,
        OBSOLETE_PLACEHOLDER_36 = 36,
        GET_THREAD_OS_INFO = 37,
        GET_CLR_DATA_INTERFACE = 38,
        MATCH_PATTERN_A = 39,
        FIND_FILE = 40,
        TYPED_DATA_OBSOLETE = 41,
        QUERY_TARGET_INTERFACE = 42,
        TYPED_DATA = 43,
        DISASSEMBLE_BUFFER = 44,
        GET_ANY_MODULE_IN_RANGE = 45,
        VIRTUAL_TO_PHYSICAL = 46,
        PHYSICAL_TO_VIRTUAL = 47,
        GET_CONTEXT_EX = 48,
        GET_TEB_ADDRESS = 128,
        GET_PEB_ADDRESS = 129
    }
}
