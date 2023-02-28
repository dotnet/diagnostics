// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SOS.Hosting
{
    /// <summary>
    /// Information flags about the state of a CLR when it is being attached
    /// to in the native pipeline debugging model
    /// </summary>
    public enum ClrDebuggingProcessFlags
    {
        // This CLR has a non-catchup managed debug event to send after jit attach is complete
        ManagedDebugEventPending = 1,
        ManagedDebugEventDebuggerLaunch = 2
    }
}
