// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    /// <summary>
    /// Defines constants for EventPipe logging sessions.
    /// </summary>
    public enum EventPipeSessionType : uint
    {
        /// <summary>
        /// The events will be written to file at the end of the session.
        /// </summary>
        TraceToFile,

        /// <summary>
        /// Events will be passed to the EventListener.
        /// </summary>
        CallbackListener,

        /// <summary>
        /// Events will be sent out-of-proc by writing them to the underlying IPC stream implementation.
        /// </summary>
        TraceToStream
    }
}
