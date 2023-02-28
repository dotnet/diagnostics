// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct WINDBG_EXTENSION_APIS /*32 or 64; both are defined the same in managed code*/
    {
        public uint nSize;
        public IntPtr lpOutputRoutine;
        public IntPtr lpGetExpressionRoutine;
        public IntPtr lpGetSymbolRoutine;
        public IntPtr lpDisasmRoutine;
        public IntPtr lpCheckControlCRoutine;
        public IntPtr lpReadProcessMemoryRoutine;
        public IntPtr lpWriteProcessMemoryRoutine;
        public IntPtr lpGetThreadContextRoutine;
        public IntPtr lpSetThreadContextRoutine;
        public IntPtr lpIoctlRoutine;
        public IntPtr lpStackTraceRoutine;
    }
}
