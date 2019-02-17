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
                "Captures memory dumps of .NET processes.", 
                new Option[] { ProcessIdOption(), OutputOption() },
                handler: CommandHandler.Create<IConsole, int, string>(new Dumper().Collect));

        private static Option ProcessIdOption() =>
            new Option(
                new[] { "-p", "--process-id" }, 
                "The ID of the process to collect a memory dump.",
                new Argument<int> { Name = "processId" });

        private static Option OutputOption() =>
            new Option(
                new[] { "-o", "--output" }, 
                "The directory to write the dump. Defaults to the current working directory.",
                new Argument<string>(Directory.GetCurrentDirectory()) { Name = "directory" });

        private static Command AnalyzeCommand() =>
            new Command(
                "analyze",
                "Start interactive dump analyze.",
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
