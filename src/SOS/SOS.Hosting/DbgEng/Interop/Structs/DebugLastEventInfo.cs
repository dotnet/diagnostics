// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public struct DEBUG_LAST_EVENT_INFO
    {
        [FieldOffset(0)]
        public DEBUG_LAST_EVENT_INFO_BREAKPOINT Breakpoint;
        [FieldOffset(0)]
        public DEBUG_LAST_EVENT_INFO_EXCEPTION Exception;
        [FieldOffset(0)]
        public DEBUG_LAST_EVENT_INFO_EXIT_THREAD ExitThread;
        [FieldOffset(0)]
        public DEBUG_LAST_EVENT_INFO_EXIT_PROCESS ExitProcess;
        [FieldOffset(0)]
        public DEBUG_LAST_EVENT_INFO_LOAD_MODULE LoadModule;
        [FieldOffset(0)]
        public DEBUG_LAST_EVENT_INFO_UNLOAD_MODULE UnloadModule;
        [FieldOffset(0)]
        public DEBUG_LAST_EVENT_INFO_SYSTEM_ERROR SystemError;
    }
}
