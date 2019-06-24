// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc
{
    public enum DiagnosticsServerCommandSet : byte
    {
        Dump           = 0x01,
        EventPipe      = 0x02,
        Profiler       = 0x03,

        Server         = 0xFF,
    }

    public enum DiagnosticsServerCommandId : byte
    {
        OK    = 0x00,
        Error = 0xFF,
    }

    public enum EventPipeCommandId : byte
    {
        StopTracing    = 0x01,
        CollectTracing = 0x02,
    }

    public enum DumpCommandId : byte
    {
        GenerateCoreDump = 0x01,
    }

    public enum ProfilerCommandId : byte
    {
        AttachProfiler = 0x01,
    }
}
