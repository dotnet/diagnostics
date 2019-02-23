using Microsoft.Diagnostic.Repl;
using Microsoft.Diagnostics.Runtime;
using System;
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

            try
            { 
                DataTarget target = null;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    target = DataTarget.LoadCoreDump(dump_path.FullName);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    target = DataTarget.LoadCrashDump(dump_path.FullName, CrashDumpReader.ClrMD);
                }
                else {
                    throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                }

                using (target)
                {
                    _consoleProvider.Out.WriteLine("Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.");
                    _consoleProvider.Out.WriteLine("Type 'quit' or 'exit' to exit the session.");

                    // Create common analyze context for commands
                    var analyzeContext = new AnalyzeContext(_consoleProvider, target, _consoleProvider.Stop) {
                        CurrentThreadId = unchecked((int)target.DataReader.EnumerateAllThreads().FirstOrDefault())
                    };
                    _commandProcessor.CommandContext = analyzeContext;

                    // Automatically enable symbol server support on Linux and MacOS
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                        analyzeContext.SOSHost.ExecuteCommand("SetSymbolServer", "-ms");
                    }

                    // Run the commands from the dotnet-dump command line
                    if (command != null)
                    {
                        foreach (string cmd in command) {
                            await _commandProcessor.Parse(cmd, _consoleProvider);
                        }
                    }

                    // Start interactive command line processing
                    await _consoleProvider.Start(async (string commandLine, CancellationToken cancellation) => {
                        analyzeContext.CancellationToken = cancellation;
                        await _commandProcessor.Parse(commandLine, _consoleProvider);
                    });
                }
            }
            catch (Exception ex) when 
                (ex is ClrDiagnosticsException ||
                 ex is FileNotFoundException || 
                 ex is DirectoryNotFoundException || 
                 ex is UnauthorizedAccessException || 
                 ex is PlatformNotSupportedException || 
                 ex is InvalidDataException ||
                 ex is InvalidOperationException ||
                 ex is NotSupportedException)
            {
                _consoleProvider.Error.WriteLine($"{ex.Message}");
                return 1;
            }

            return 0;
        }
    }
}
