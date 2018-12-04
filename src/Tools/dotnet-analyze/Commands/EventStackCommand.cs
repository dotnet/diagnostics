using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Microsoft.Diagnostics.Tools.Analyze.Commands
{
    public class EventStackCommand : TraceCommandBase
    {
        public override IReadOnlyList<string> Names => new[] { "eventstack" };

        public override string Description => "Dumps the stack trace associated with an event, if there is one.";

        protected override Task RunAsyncCore(IConsole console, string[] args, AnalysisSession session, TraceLog trace)
        {
            if (args.Length < 1)
            {
                console.Error.WriteLine("Usage: eventstack <eventIndex>");
                return Task.CompletedTask;
            }

            if (!int.TryParse(args[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var eventIndex))
            {
                console.Error.WriteLine("Usage: eventstack <eventIndex>");
                return Task.CompletedTask;
            }

            var evt = trace.Events.ElementAt(eventIndex);
            var stack = evt.CallStack();
            if (stack != null)
            {
                WriteStack(stack, console);
            }
            else
            {
                console.Error.WriteLine($"Unable to find any call stacks for event {eventIndex:X4}!");
            }

            return Task.CompletedTask;
        }

        private void WriteStack(TraceCallStack stack, IConsole console)
        {
            while (stack != null)
            {
                console.WriteLine($"  at {stack.CodeAddress.ModuleName}!{stack.CodeAddress.FullMethodName} + 0x{stack.CodeAddress.ILOffset:X4}");
                stack = stack.Caller;
            }
        }

        public override Task WriteHelpAsync(IConsole console)
        {
            console.WriteLine("TODO");
            return Task.CompletedTask;
        }
    }
}
