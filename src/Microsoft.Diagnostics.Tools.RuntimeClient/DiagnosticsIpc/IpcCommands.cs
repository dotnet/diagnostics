using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc
{
    public enum DiagnosticServerCommandSet : byte
    {
        Dump           = 0x01,
        EventPipe      = 0x02,
        Profiler       = 0x03,

        Server         = 0xFF,
    }

    public enum DiagnosticServerCommandId : byte
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
