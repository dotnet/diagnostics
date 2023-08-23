// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal readonly struct MinidumpModule
    {
        public readonly ulong BaseOfImage;
        public readonly int SizeOfImage;
        public readonly uint CheckSum;
        public readonly int DateTimeStamp;
        public readonly uint ModuleNameRva;
        public readonly FixedFileInfo VersionInfo;
        public readonly MinidumpLocationDescriptor CvRecord;
        public readonly MinidumpLocationDescriptor MiscRecord;
        private readonly ulong _reserved0;
        private readonly ulong _reserved1;
    }
}