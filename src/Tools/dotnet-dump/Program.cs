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
                "Capture one or more dumps (core files on Mac/Linux) from a process.", 
                new Option[] { ProcessIdOption(), IntervalOption(), NumberOption(), OutputOption(), TypeOption() },
                handler: CommandHandler.Create<IConsole, int, int, int, string, string>(new Dumper().Collect));

        private static Option ProcessIdOption() =>
            new Option(
                new[] { "-p", "--process-id" }, 
                "The the process to collect a memory dump.",
                new Argument<int> { Name = "pid" });

        private static Option IntervalOption() =>
            new Option(
                "--interval-sec",
                "The number of seconds to wait between collecting each dump. Defaults to 10 seconds if not specified.",
                new Argument<int>() { Name = "seconds" });

        private static Option NumberOption() =>
            new Option(
                "--number", 
                "The number of dumps to collect from the target process. Defaults to 1 if not specified.",
                new Argument<int>() { Name = "number_of_dumps" });

        private static Option OutputOption() =>
            new Option(
                new[] { "-o", "--output" },
                @"The path where collected dumps should be written. Defaults to '.\dump_YYYYMMDD_HHMMSS.dmp' on Windows and 
'.\core_YYYYMMDD_HHMMSS' on Linux where YYYYMMDD is Year/Month/Day and HHMMSS is Hour/Minute/Second. This 
option is a directory if an existing directory, ends with '\' or '/', or if the --number or --interval-sec
options are specified, otherwise it is an exact file name.",
                new Argument<string>(Directory.GetCurrentDirectory()) { Name = "output_dump_path" });

        private static Option TypeOption() =>
            new Option(
                "--type",
                @"The dump type determines the kinds of information that are collected from the process. There are two types:

heap - A large and relatively comprehensive dump containing module lists, thread lists, all stacks,
       exception information, handle information, and all memory except for mapped images.
triage - A small dump containing module lists, thread lists, exception information and all stacks.

If not specified 'heap' is the default.",
                new Argument<string>("heap") { Name = "dump_type" });

        private static Command AnalyzeCommand() =>
            new Command(
                "analyze",
                "Starts an interactive shell with debugging commands to explore a dump.",
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
                new Argument<string[]>() { Name = "command" });
    }
}
