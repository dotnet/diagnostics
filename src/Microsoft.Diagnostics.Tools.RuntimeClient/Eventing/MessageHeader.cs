// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.Eventing
{
    [StructLayout(LayoutKind.Sequential)]
    struct MessageHeader
    {
        public DiagnosticMessageType RequestType;

        public uint Pid;
    }
}
