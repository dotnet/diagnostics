// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_THREAD_BASIC_INFORMATION
    {
        public DEBUG_TBINFO Valid;
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