// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_MANSTR : uint
    {
        NONE = 0,
        LOADED_SUPPORT_DLL = 1,
        LOAD_STATUS = 2
    }
}
