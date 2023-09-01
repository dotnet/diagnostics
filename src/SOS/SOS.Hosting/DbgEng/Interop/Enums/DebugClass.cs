// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_CLASS : uint
    {
        UNINITIALIZED = 0,
        KERNEL = 1,
        USER_WINDOWS = 2,
        IMAGE_FILE = 3
    }
}
