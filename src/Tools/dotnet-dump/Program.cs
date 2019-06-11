using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Tools.Dump
{
    class Program
    {
        public static Task<int> Main(string[] args)
        {
            var parser = new CommandLineBuilder()
                .AddCommand(CollectCommand())
                .AddCommand(AnalyzeCommand())
                .UseDefaults()
                .Build();

            return parser.InvokeAsync(args);
        }

        private static Command CollectCommand() =>
            new Command(
                "collect", 
                "Capture dumps from a process", 
                new Option[] { ProcessIdOption(), OutputOption(), TypeOption() },
                handler: CommandHandler.Create<IConsole, int, string, Dumper.DumpTypeOption>(new Dumper().Collect));

        private static Option ProcessIdOption() =>
            new Option(
                new[] { "-p", "--process-id" }, 
                "The process to collect a memory dump from.",
                new Argument<int> { Name = "pid" });


        private static Option OutputOption() =>
            new Option(
                new[] { "-o", "--output" },
                @"The path where collected dumps should be written. Defaults to '.\dump_YYYYMMDD_HHMMSS.dmp' on Windows and 
'./core_YYYYMMDD_HHMMSS' on Linux where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. Otherwise, it is the full
path and file name of the dump.",
                new Argument<string>() { Name = "output_dump_path" });

        private static Option TypeOption() =>
            new Option(
                "--type",
                @"The dump type determines the kinds of information that are collected from the process. There are two types:

heap - A large and relatively comprehensive dump containing module lists, thread lists, all stacks,
       exception information, handle information, and all memory except for mapped images.
mini - A small dump containing module lists, thread lists, exception information and all stacks.

If not specified 'heap' is the default.",
                new Argument<Dumper.DumpTypeOption>(Dumper.DumpTypeOption.Heap) { Name = "dump_type" });

        private static Command AnalyzeCommand() =>
            new Command(
                "analyze",
                "Starts an interactive shell with debugging commands to explore a dump",
                new Option[] { RunCommand() }, argument: DumpPath(),
                handler: CommandHandler.Create<FileInfo, string[]>(new Analyzer().Analyze));

        private static Argument DumpPath() =>
            new Argument<FileInfo> {
                Name = "dump_path",
                Description = "Name of the dump file to analyze." }.ExistingOnly();

        private static Option RunCommand() =>
            new Option(
                new[] { "-c", "--command" },
                "Run the command on start.",
                new Argument<string[]>() { Name = "command", Arity = ArgumentArity.ZeroOrMore });
    }
}
