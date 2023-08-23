// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Float in X86-specific windows thread context.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public readonly struct Float80
    {
        [FieldOffset(0x0)]
        public readonly ulong Mantissa;

        [FieldOffset(0x8)]
        public readonly ushort Exponent;
    }
}
