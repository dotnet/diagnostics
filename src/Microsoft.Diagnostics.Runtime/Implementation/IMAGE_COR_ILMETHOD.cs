// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct IMAGE_COR_ILMETHOD
    {
        public uint FlagsSizeStack;
        public uint CodeSize;
        public uint LocalVarSignatureToken;

        public const uint FormatShift = 3;
        public const uint FormatMask = (uint)(1 << (int)FormatShift) - 1;
        public const uint TinyFormat = 0x2;
        public const uint mdSignatureNil = 0x11000000;
    }
}