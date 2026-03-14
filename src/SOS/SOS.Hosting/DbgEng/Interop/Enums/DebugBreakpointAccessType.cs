// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_BREAKPOINT_ACCESS_TYPE : uint
    {
        READ = 1,
        WRITE = 2,
        EXECUTE = 4,
        IO = 8
    }
}
