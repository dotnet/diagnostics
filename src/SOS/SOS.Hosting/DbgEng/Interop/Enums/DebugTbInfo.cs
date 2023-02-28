// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_TBINFO : uint
    {
        NONE = 0,
        EXIT_STATUS = 1,
        PRIORITY_CLASS = 2,
        PRIORITY = 4,
        TIMES = 8,
        START_OFFSET = 0x10,
        AFFINITY = 0x20,
        ALL = 0x3f
    }
}
