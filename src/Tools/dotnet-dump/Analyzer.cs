using Microsoft.Diagnostic.Repl;
using Microsoft.Diagnostics.Runtime;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostic.Tools.Dump
{
    public class Analyzer
    {
        private readonly ConsoleProvider _consoleProvider;
        private readonly CommandProcessor _commandProcessor;

        public Analyzer()
        {
            _consoleProvider = new ConsoleProvider();
            _commandProcessor = new CommandProcessor(new Assembly[] { typeof(Analyzer).Assembly });
        }

        public async Task<int> Analyze(FileInfo dump_path, string[] command)
        {
            _consoleProvider.Out.WriteLine($"Loading core dump: {dump_path} ...");

            DataTarget target = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                target = DataTarget.LoadCoreDump(dump_path.FullName);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                target = DataTarget.LoadCrashDump(dump_path.FullName, CrashDumpReader.ClrMD);
            }
            else {
                _consoleProvider.Error.WriteLine($"{RuntimeInformation.OSDescription} not supported");
                return 1;
            }

            using (target)
            {
                // Create common analyze context for commands
                var analyzeContext = new AnalyzeContext(_consoleProvider, target, _consoleProvider.Stop) {
                    CurrentThreadId = unchecked((int)target.DataReader.EnumerateAllThreads().FirstOrDefault())
                };
                _commandProcessor.CommandContext = analyzeContext;

                // Automatically enable symbol server support on Linux and MacOS
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    await _commandProcessor.Parse("setsymbolserver -ms", _consoleProvider);
                }

                // Run the commands from the dotnet-dump command line
                if (command != null)
                {
                    foreach (string cmd in command)
                    {
                        await _commandProcessor.Parse(cmd, _consoleProvider);
                    }
                }

                // Start interactive command line processing
                await _consoleProvider.Start(async (string commandLine, CancellationToken cancellation) => {
                    analyzeContext.CancellationToken = cancellation;
                    await _commandProcessor.Parse(commandLine, _consoleProvider);
                });
            }

            return 0;
        }
    }
}
