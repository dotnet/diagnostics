// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    /// <summary>
    /// Message header used to send commands to the .NET Core runtime through IPC.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MessageHeader
    {
        /// <summary>
        /// Request type.
        /// </summary>
        public DiagnosticsMessageType RequestType;

        /// <summary>
        /// Remote process Id.
        /// </summary>
        public uint Pid;
    }
}
