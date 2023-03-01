// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct WDBGEXTS_THREAD_OS_INFO
    {
        public uint ThreadId;
        public uint ExitStatus;
        public uint PriorityClass;
        public uint Priority;
        public ulong CreateTime;
        public ulong ExitTime;
        public ulong KernelTime;
        public ulong UserTime;
        public ulong StartOffset;
        public ulong Affinity;
    }
}
