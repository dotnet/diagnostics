// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_ECREATE_PROCESS : uint
    {
        DEFAULT = 0,
        INHERIT_HANDLES = 1,
        USE_VERIFIER_FLAGS = 2,
        USE_IMPLICIT_COMMAND_LINE = 4
    }
}
