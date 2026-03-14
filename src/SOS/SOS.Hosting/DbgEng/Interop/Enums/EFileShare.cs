// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum EFileShare : uint
    {
        None = 0x00000000,
        Read = 0x00000001,
        Write = 0x00000002,
        Delete = 0x00000004
    }
}
