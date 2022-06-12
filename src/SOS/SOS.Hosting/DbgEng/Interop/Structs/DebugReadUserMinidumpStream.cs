// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_READ_USER_MINIDUMP_STREAM
    {
        public uint StreamType;
        public uint Flags;
        public ulong Offset;
        public IntPtr Buffer;
        public uint BufferSize;
        public uint BufferUsed;
    }
}