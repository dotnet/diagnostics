// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_OUTCTL : uint
    {
        THIS_CLIENT = 0,
        ALL_CLIENTS = 1,
        ALL_OTHER_CLIENTS = 2,
        IGNORE = 3,
        LOG_ONLY = 4,
        SEND_MASK = 7,
        NOT_LOGGED = 8,
        OVERRIDE_MASK = 0x10,
        DML = 0x20,
        AMBIENT_DML = 0xfffffffe,
        AMBIENT_TEXT = 0xffffffff
    }
}
