// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_REGISTER_DESCRIPTION
    {
        public DEBUG_VALUE_TYPE Type;
        public DEBUG_REGISTER Flags;
        public ulong SubregMaster;
        public ulong SubregLength;
        public ulong SubregMask;
        public ulong SubregShift;
        public ulong Reserved0;
    }
}