using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.Stacks.Formats;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Collect
{
    public abstract class EventCollector
    {
        protected CollectionConfiguration Config { get; }

        protected EventCollector(CollectionConfiguration config) => Config = config;

        public abstract Task StartCollectingAsync();
        public abstract Task StopCollectingAsync();

        protected abstract StackSource GetStackSource(SymbolReader symbolReader);

        public async Task FormatOutputAsync()
        {
            if (!Config.OutputFormat.Equals("SpeedScope", StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(7)); // we need to wait for a moment to make sure the data gets written to the trace file

            var symbolReader = new SymbolReader(Console.Out) { SymbolPath = SymbolPath.MicrosoftSymbolServerPath };
            var stackSource = GetStackSource(symbolReader);

            var speedScopeFilePath = Path.Combine(Config.OutputPath, $"{Config.ProcessId}.speedscope.json");

            SpeedScopeStackSourceWriter.WriteStackViewAsJson(stackSource, speedScopeFilePath);
        }
    }
}
