// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public struct LANGANDCODEPAGE
    {
        [FieldOffset(0)]
        public ushort wLanguage;
        [FieldOffset(2)]
        public ushort wCodePage;
    }
}
