// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_SYSOBJINFO : uint
    {
        THREAD_BASIC_INFORMATION = 0,
        THREAD_NAME_WIDE = 1,
        CURRENT_PROCESS_COOKIE = 2
    }
}
