// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace SOS.Hosting.DbgEng.Interop
{
    [Flags]
    public enum DEBUG_FIND_SOURCE : uint
    {
        DEFAULT = 0,
        FULL_PATH = 1,
        BEST_MATCH = 2,
        NO_SRCSRV = 4,
        TOKEN_LOOKUP = 8
    }
}
