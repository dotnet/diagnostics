// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_STATUS : uint
    {
        NO_CHANGE = 0,
        GO = 1,
        GO_HANDLED = 2,
        GO_NOT_HANDLED = 3,
        STEP_OVER = 4,
        STEP_INTO = 5,
        BREAK = 6,
        NO_DEBUGGEE = 7,
        STEP_BRANCH = 8,
        IGNORE_EVENT = 9,
        RESTART_REQUESTED = 10,
        REVERSE_GO = 11,
        REVERSE_STEP_BRANCH = 12,
        REVERSE_STEP_OVER = 13,
        REVERSE_STEP_INTO = 14,
        OUT_OF_SYNC = 15,
        WAIT_INPUT = 16,
        TIMEOUT = 17,
        MASK = 0x1f
    }
}
