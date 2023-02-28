// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_END : uint
    {
        PASSIVE = 0,
        ACTIVE_TERMINATE = 1,
        ACTIVE_DETACH = 2,
        END_REENTRANT = 3,
        END_DISCONNECT = 4
    }
}
