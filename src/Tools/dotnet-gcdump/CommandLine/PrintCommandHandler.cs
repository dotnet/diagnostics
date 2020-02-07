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
        delegate Task<int> PrintDelegate(CancellationToken ct, IConsole console, FileInfo file);
        
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

        private static Task<int> Print(CancellationToken ct, IConsole console, FileInfo file)
        {
            if (file == null)
            {
                Console.Error.WriteLine($"-f|--file is required");
                return Task.FromResult(-1);
            }
            if (!file.Exists)
            {
                Console.Error.WriteLine($"Invalid gcdump file {file}");
                return Task.FromResult(-1);
            }

            try
            {
                var dump = GCHeapDump.ReadMemoryGraph(file.FullName);
                dump.MemoryGraph.WriteToStdOut();
                return Task.FromResult(0);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An error occured while parsing the file, {e.GetBaseException().Message}");
                return Task.FromResult(-1);
            }
        }

        private static Option<FileInfo> FileNameOption() =>
            new Option<FileInfo>(new[] {"-f", "--file"}, "The file to read gcdump from.");
    }
}