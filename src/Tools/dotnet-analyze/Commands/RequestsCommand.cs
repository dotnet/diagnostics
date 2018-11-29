using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public class RequestsCommand : TraceCommandBase
    {
        public override IReadOnlyList<string> Names => new[] { "requests" };

        public override string Description => "Lists all ASP.NET Core HTTP requests contained in the attached trace (if there is one).";

        protected override Task RunAsyncCore(IConsole console, string[] args, AnalysisSession session, TraceLog trace)
        {
            console.WriteLine("Scanning request events...");
            var requests = trace.Events
                .Where(t => string.Equals(t.ProviderName, "Microsoft-AspNetCore-Hosting") && string.Equals(t.EventName, "RequestStart/Start"))
                .Select(e => e.Clone())
                .ToList();
            console.WriteLine("HTTP requests:");
            foreach (var request in requests)
            {
                console.WriteLine($"* ({((int)request.EventIndex).ToString("X4")}) [{request.TimeStamp:O}] {request.PayloadString(0)} {request.PayloadString(1)}");
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
