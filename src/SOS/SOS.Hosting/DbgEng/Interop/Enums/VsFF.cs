// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum VS_FF : uint
    {
        DEBUG = 0x00000001,
        PRERELEASE = 0x00000002,
        PATCHED = 0x00000004,
        PRIVATEBUILD = 0x00000008,
        INFOINFERRED = 0x00000010,
        SPECIALBUILD = 0x00000020
    }
}
