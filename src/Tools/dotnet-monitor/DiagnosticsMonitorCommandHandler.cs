using Microsoft.Diagnostics.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal sealed class DiagnosticsMonitorCommandHandler
    {
        public async Task<int> Start(IConsole console, int processId, int refreshInterval)
        {
            DiagnosticsMonitor monitor = new DiagnosticsMonitor(new MonitoringSourceConfiguration(), new ConsoleSinkConfiguration(), NullLogger.Instance);

            await monitor.ProcessEvents("default", Environment.MachineName, processId, CancellationToken.None);

            return 0;
        }
    }
}
