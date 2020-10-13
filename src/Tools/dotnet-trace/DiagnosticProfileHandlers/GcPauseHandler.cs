using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.ComponentModel;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Trace.DiagnosticProfileHandlers
{
    /// <summary>
    /// This class handles parsing for GC-triggered Pauses - specifically by parsing the GC/SuspendEEStart and GC/SuspendEEStop events.
    /// 
    /// </summary>
    internal class GcPauseHandler : IDiagnosticProfileHandler
    {
        private string option;

        public GcPauseHandler(string gcHandlerOption)
        {
            option = gcHandlerOption;
        }

        public void AddHandler(EventPipeEventSource source)
        {
            source.Clr.GCStart += (GCStartTraceData gcStartData) =>
            {
                Console.WriteLine($"GC {gcStartData.Count} Start: {gcStartData.Dump(true)}");
            };

            source.Clr.GCStop += (GCEndTraceData gcEndData) =>
            {
                Console.WriteLine($"GC End: {gcEndData.Dump(true)}");
            };

            source.Clr.GCSuspendEEStart += (GCSuspendEETraceData data) =>
            {
                Console.WriteLine($"GC SuspendEEStart: {data.Dump(true)}");
            };

            source.Clr.GCSuspendEEStop += (GCNoUserDataTraceData data) =>
            {
                Console.WriteLine($"GC SuspendEEStop: {data.Dump(true)}");
            };
        }
    }
}
