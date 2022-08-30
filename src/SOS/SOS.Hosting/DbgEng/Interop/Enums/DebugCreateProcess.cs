// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_CREATE_PROCESS : uint
    {
        DEFAULT = 0,
        NO_DEBUG_HEAP = 0x00000400, /* CREATE_UNICODE_ENVIRONMENT */
        THROUGH_RTL = 0x00010000 /* STACK_SIZE_PARAM_IS_A_RESERVATION */
    }
}