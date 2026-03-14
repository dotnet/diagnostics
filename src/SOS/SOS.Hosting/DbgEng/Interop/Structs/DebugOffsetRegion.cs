// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct DEBUG_OFFSET_REGION
    {
        private readonly ulong _base;
        private readonly ulong _size;
    }
}
