using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public class LogsCommand : TraceCommandBase
    {
        public override IReadOnlyList<string> Names => new[] { "logs" };

        public override string Description => "Dumps Microsoft.Extensions.Logging logs.";

        protected override Task RunAsyncCore(IConsole console, string[] args, AnalysisSession session, TraceLog trace)
        {
            var prefix = string.Empty;
            if(args.Length > 0) {
                prefix = args[0];
            }
            console.WriteLine("Scanning log events...");
            var events = trace.Events
                .Where(t => 
                    string.Equals(t.ProviderName, "Microsoft-Extensions-Logging") &&
                    string.Equals(t.EventName, "MessageJson") &&
                    ((string)t.PayloadByName("LoggerName")).StartsWith(prefix))
                .Select(e => e.Clone())
                .ToList();
            console.WriteLine("Logs:");
            foreach (var evt in events)
            {
                var log = LogMessage.Load(evt);
                console.WriteLine($"* ({((int)evt.EventIndex).ToString("X4")}) [{log.Timestamp:O}] [{log.Level}] {log.LoggerName}({log.EventId}): {log.Message}");
                foreach(var (key, value) in log.Arguments)
                {
                    console.WriteLine($"    {key} = {value}");
                }
            }

            return Task.CompletedTask;
        }

        public override Task WriteHelpAsync(IConsole console)
        {
            console.WriteLine("TODO");
            return Task.CompletedTask;
        }
    }
}
