using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Logs
{
    internal class LogViewerServiceOptions : LoggerFilterOptions
    {
        public int ProcessId { get; set; }
    }
}