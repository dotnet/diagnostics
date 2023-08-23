// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal readonly struct FixedFileInfo
    {
        public readonly uint Signature;            /* e.g. 0xfeef04bd */
        public readonly uint StrucVersion;         /* e.g. 0x00000042 = "0.42" */

        public readonly ushort Minor;
        public readonly ushort Major;
        public readonly ushort Patch;
        public readonly ushort Revision;

        public readonly uint ProductVersionMS;     /* e.g. 0x00030010 = "3.10" */
        public readonly uint ProductVersionLS;     /* e.g. 0x00000031 = "0.31" */
        public readonly uint FileFlagsMask;        /* = 0x3F for version "0.42" */
        public readonly uint FileFlags;            /* e.g. VFF_DEBUG | VFF_PRERELEASE */
        public readonly uint FileOS;               /* e.g. VOS_DOS_WINDOWS16 */
        public readonly uint FileType;             /* e.g. VFT_DRIVER */
        public readonly uint FileSubtype;          /* e.g. VFT2_DRV_KEYBOARD */

        // Timestamps would be useful, but they're generally missing (0).
        public readonly uint FileDateMS;           /* e.g. 0 */
        public readonly uint FileDateLS;           /* e.g. 0 */

        public Version AsVersionInfo() => new(Major, Minor, Revision, Patch);
    }
}