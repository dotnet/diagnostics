// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_TYPEOPTS : uint
    {
        UNICODE_DISPLAY = 1,
        LONGSTATUS_DISPLAY = 2,
        FORCERADIX_OUTPUT = 4,
        MATCH_MAXSIZE = 8
    }
}
