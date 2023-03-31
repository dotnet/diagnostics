// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_HANDLE_DATA_BASIC
    {
        public uint TypeNameSize;
        public uint ObjectNameSize;
        public uint Attributes;
        public uint GrantedAccess;
        public uint HandleCount;
        public uint PointerCount;
    }
}
