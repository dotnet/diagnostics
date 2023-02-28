// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting.DbgEng.Interop
{
    public enum DEBUG_STATUS_FLAGS : ulong
    {
        /// <summary>
        /// This bit is added in DEBUG_CES_EXECUTION_STATUS notifications when the
        /// engines execution status is changing due to operations performed during a
        /// wait, such as making synchronous callbacks. If the bit is not set the
        /// execution status is changing due to a wait being satisfied.
        /// </summary>
        INSIDE_WAIT = 0x100000000,

        /// <summary>
        /// This bit is added in DEBUG_CES_EXECUTION_STATUS notifications when the
        /// engines execution status update is coming after a wait has timed-out. It
        /// indicates that the execution status change was not due to an actual event.
        /// </summary>
        WAIT_TIMEOUT = 0x200000000
    }
}
