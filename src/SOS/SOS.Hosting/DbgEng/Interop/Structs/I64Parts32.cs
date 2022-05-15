// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public struct I64PARTS32
    {
        [FieldOffset(0)]
        public uint LowPart;
        [FieldOffset(4)]
        public uint HighPart;
    }
}