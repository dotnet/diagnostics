// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_BREAKPOINT_FLAG : uint
    {
        GO_ONLY = 1,
        DEFERRED = 2,
        ENABLED = 4,
        ADDER_ONLY = 8,
        ONE_SHOT = 0x10
    }
}