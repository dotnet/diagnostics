// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_OUTSYM : uint
    {
        DEFAULT = 0,
        FORCE_OFFSET = 1,
        SOURCE_LINE = 2,
        ALLOW_DISPLACEMENT = 4
    }
}
