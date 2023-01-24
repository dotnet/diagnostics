// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_REQUEST : uint
    {
        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - Unused.
        /// </summary>
        SOURCE_PATH_HAS_SOURCE_SERVER = 0,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - Machine-specific CONTEXT.
        /// </summary>
        TARGET_EXCEPTION_CONTEXT = 1,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - ULONG system ID of thread.
        /// </summary>
        TARGET_EXCEPTION_THREAD = 2,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - EXCEPTION_RECORD64.
        /// </summary>
        TARGET_EXCEPTION_RECORD = 3,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - DEBUG_CREATE_PROCESS_OPTIONS.
        /// </summary>
        GET_ADDITIONAL_CREATE_OPTIONS = 4,

        /// <summary>
        /// InBuffer - DEBUG_CREATE_PROCESS_OPTIONS.
        /// OutBuffer - Unused.
        /// </summary>
        SET_ADDITIONAL_CREATE_OPTIONS = 5,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - ULONG[2] major/minor.
        /// </summary>
        GET_WIN32_MAJOR_MINOR_VERSIONS = 6,

        /// <summary>
        /// InBuffer - DEBUG_READ_USER_MINIDUMP_STREAM.
        /// OutBuffer - Unused.
        /// </summary>
        READ_USER_MINIDUMP_STREAM = 7,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - Unused.
        /// </summary>
        TARGET_CAN_DETACH = 8,

        /// <summary>
        /// InBuffer - PTSTR.
        /// OutBuffer - Unused.
        /// </summary>
        SET_LOCAL_IMPLICIT_COMMAND_LINE = 9,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - Event code stream offset.
        /// </summary>
        GET_CAPTURED_EVENT_CODE_OFFSET = 10,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - Event code stream information.
        /// </summary>
        READ_CAPTURED_EVENT_CODE_STREAM = 11,

        /// <summary>
        /// InBuffer - Input data block.
        /// OutBuffer - Processed data block.
        /// </summary>
        EXT_TYPED_DATA_ANSI = 12,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - Returned path.
        /// </summary>
        GET_EXTENSION_SEARCH_PATH_WIDE = 13,

        /// <summary>
        /// InBuffer - DEBUG_GET_TEXT_COMPLETIONS_IN.
        /// OutBuffer - DEBUG_GET_TEXT_COMPLETIONS_OUT.
        /// </summary>
        GET_TEXT_COMPLETIONS_WIDE = 14,

        /// <summary>
        /// InBuffer - ULONG64 cookie.
        /// OutBuffer - DEBUG_CACHED_SYMBOL_INFO.
        /// </summary>
        GET_CACHED_SYMBOL_INFO = 15,

        /// <summary>
        /// InBuffer - DEBUG_CACHED_SYMBOL_INFO.
        /// OutBuffer - ULONG64 cookie.
        /// </summary>
        ADD_CACHED_SYMBOL_INFO = 16,

        /// <summary>
        /// InBuffer - ULONG64 cookie.
        /// OutBuffer - Unused.
        /// </summary>
        REMOVE_CACHED_SYMBOL_INFO = 17,

        /// <summary>
        /// InBuffer - DEBUG_GET_TEXT_COMPLETIONS_IN.
        /// OutBuffer - DEBUG_GET_TEXT_COMPLETIONS_OUT.
        /// </summary>
        GET_TEXT_COMPLETIONS_ANSI = 18,

        /// <summary>
        /// InBuffer - Unused.
        /// OutBuffer - Unused.
        /// </summary>
        CURRENT_OUTPUT_CALLBACKS_ARE_DML_AWARE = 19,

        /// <summary>
        /// InBuffer - ULONG64 offset.
        /// OutBuffer - Unwind information.
        /// </summary>
        GET_OFFSET_UNWIND_INFORMATION = 20,

        /// <summary>
        /// InBuffer - Unused
        /// OutBuffer - returned DUMP_HEADER32/DUMP_HEADER64 structure.
        /// </summary>
        GET_DUMP_HEADER = 21,

        /// <summary>
        /// InBuffer - DUMP_HEADER32/DUMP_HEADER64 structure.
        /// OutBuffer - Unused
        /// </summary>
        SET_DUMP_HEADER = 22,

        /// <summary>
        /// InBuffer - Midori specific
        /// OutBuffer - Midori specific
        /// </summary>
        MIDORI = 23,

        /// <summary>
        /// InBuffer - Unused
        /// OutBuffer - PROCESS_NAME_ENTRY blocks
        /// </summary>
        PROCESS_DESCRIPTORS = 24,

        /// <summary>
        /// InBuffer - Unused
        /// OutBuffer - MINIDUMP_MISC_INFO_N blocks
        /// </summary>
        MISC_INFORMATION = 25,

        /// <summary>
        /// InBuffer - Unused
        /// OutBuffer - ULONG64 as TokenHandle value
        /// </summary>
        OPEN_PROCESS_TOKEN = 26,

        /// <summary>
        /// InBuffer - Unused
        /// OutBuffer - ULONG64 as TokenHandle value
        /// </summary>
        OPEN_THREAD_TOKEN = 27,

        /// <summary>
        /// InBuffer -  ULONG64 as TokenHandle being duplicated
        /// OutBuffer - ULONG64 as new duplicated TokenHandle
        /// </summary>
        DUPLICATE_TOKEN = 28,

        /// <summary>
        /// InBuffer - a ULONG64 as TokenHandle and a ULONG as NtQueryInformationToken() request code
        /// OutBuffer - NtQueryInformationToken() return
        /// </summary>
        QUERY_INFO_TOKEN = 29,

        /// <summary>
        /// InBuffer - ULONG64 as TokenHandle
        /// OutBuffer - Unused
        /// </summary>
        CLOSE_TOKEN = 30,

        /// <summary>
        /// InBuffer - ULONG64 for process server identification and ULONG as PID
        /// OutBuffer - Unused
        /// </summary>
        WOW_PROCESS = 31,

        /// <summary>
        /// InBuffer - ULONG64 for process server identification and PWSTR as module path
        /// OutBuffer - Unused
        /// </summary>
        WOW_MODULE = 32,

        /// <summary>
        /// InBuffer - Unused
        /// OutBuffer - Unused
        /// return - S_OK if non-invasive user-mode attach, S_FALSE if not (but still live user-mode), E_FAIL otherwise.
        /// </summary>
        LIVE_USER_NON_INVASIVE = 33,

        /// <summary>
        /// InBuffer - TID
        /// OutBuffer - Unused
        /// return - ResumeThreads() return.
        /// </summary>
        RESUME_THREAD = 34
    }
}