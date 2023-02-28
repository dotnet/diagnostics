// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_CLASS_QUALIFIER : uint
    {
        KERNEL_CONNECTION = 0,
        KERNEL_LOCAL = 1,
        KERNEL_EXDI_DRIVER = 2,
        KERNEL_IDNA = 3,
        KERNEL_SMALL_DUMP = 1024,
        KERNEL_DUMP = 1025,
        KERNEL_FULL_DUMP = 1026,
        USER_WINDOWS_PROCESS = 0,
        USER_WINDOWS_PROCESS_SERVER = 1,
        USER_WINDOWS_IDNA = 2,
        USER_WINDOWS_SMALL_DUMP = 1024,
        USER_WINDOWS_DUMP = 1026
    }
}
