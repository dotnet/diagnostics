using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    internal static class PrintCommandHandler
    {
        delegate Task<int> PrintDelegate(CancellationToken ct, IConsole console, string file);
        
        public static Command PrintCommand() =>
            new Command(
                name: "print",
                description: "Prints a previously collected gcdump into the stdout")
            {
                // Handler
                HandlerDescriptor.FromDelegate((PrintDelegate) Print).GetCommandHandler(),
                // Options
                FileNameOption()
            };

        private static Task<int> Print(CancellationToken ct, IConsole console, string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                Console.Error.WriteLine($"-f|--file is required");
                return Task.FromResult(-1);
            }
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"Invalid gcdump file {file}");
                return Task.FromResult(-1);
            }

            try
            {
                var dump = GCHeapDump.ReadMemoryGraph(file);
                dump.MemoryGraph.WriteToStdOut();
                return Task.FromResult(0);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An error occured while parsing the file, {e.GetBaseException().Message}");
                return Task.FromResult(-1);
            }
        }
        
        private static Option FileNameOption() =>
            new Option(
                aliases: new[] { "-f", "--file" },
                description: "The file to read gcdump from.")
            {
                Argument = new Argument<string>(name: "file")
            };
    }
}