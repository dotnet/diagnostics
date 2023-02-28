// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_CES_EXECUTION_STATUS : ulong
    {
        INSIDE_WAIT = 0x100000000UL,
        WAIT_TIMEOUT = 0x200000000UL
    }
}
