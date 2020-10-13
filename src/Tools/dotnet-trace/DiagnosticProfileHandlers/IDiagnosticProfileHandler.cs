using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Trace.DiagnosticProfileHandlers
{
    internal interface IDiagnosticProfileHandler
    {
        public void AddHandler(EventPipeEventSource source);
    }
}
