// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal readonly struct MinidumpHeader
    {
        public const int ExpectedMagic1 = 0x504d444d;
        public const int ExpectedMagic2 = 0xa793;

        public bool IsValid => Magic1 == ExpectedMagic1 && Magic2 == ExpectedMagic2;

        public readonly uint Magic1;
        public readonly ushort Magic2;
        public readonly ushort Version;
        public readonly uint NumberOfStreams;
        public readonly uint StreamDirectoryRva;
        public readonly uint CheckSum;
        public readonly uint TimeDateStamp;
        public readonly ulong Flags;
    }
}
