// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_PROCESS : uint
    {
        DEFAULT = 0,
        DETACH_ON_EXIT = 1,
        ONLY_THIS_PROCESS = 2
    }
}
