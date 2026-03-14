// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct IMAGE_COR20_HEADER_ENTRYPOINT
    {
        [FieldOffset(0)]
        public readonly uint Token;
        [FieldOffset(0)]
        public readonly uint RVA;
    }
}
