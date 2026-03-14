// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum CODE_PAGE : uint
    {
        ACP = 0, // default to ANSI code page
        OEMCP = 1, // default to OEM  code page
        MACCP = 2, // default to MAC  code page
        THREAD_ACP = 3, // current thread's ANSI code page
        SYMBOL = 42, // SYMBOL translations

        UTF7 = 65000, // UTF-7 translation
        UTF8 = 65001 // UTF-8 translation
    }
}
