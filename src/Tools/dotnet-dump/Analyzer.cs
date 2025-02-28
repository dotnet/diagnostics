// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using SOS.Hosting;

namespace Microsoft.Diagnostics.Tools.Dump
{
    public class Analyzer : Host, ISettingsService
    {
        private readonly ConsoleService _consoleService;
        private readonly FileLoggingConsoleService _fileLoggingConsoleService;
        private readonly CommandService _commandService;

        public Analyzer()
            : base(HostType.DotnetDump)
        {
            DiagnosticLoggingService.Initialize();

            _consoleService = new ConsoleService();
            _fileLoggingConsoleService = new FileLoggingConsoleService(_consoleService);
            DiagnosticLoggingService.Instance.SetConsole(_fileLoggingConsoleService, _fileLoggingConsoleService);

            _commandService = new CommandService();
            ServiceManager.NotifyExtensionLoad.Register(_commandService.AddCommands);
        }

        public Task<int> Analyze(FileInfo dump_path, string[] command)
        {
            _fileLoggingConsoleService.WriteLine($"Loading core dump: {dump_path} ...");

            // Attempt to load the persisted command history
            string historyFileName = null;
            try
            {
                historyFileName = Path.Combine(Utilities.GetDotNetHomeDirectory(), "dotnet-dump.history");
                string[] history = File.ReadAllLines(historyFileName);
                _consoleService.AddCommandHistory(history);
            }
            catch (Exception ex) when
                (ex is IOException
                 or ArgumentNullException
                 or UnauthorizedAccessException
                 or NotSupportedException
                 or SecurityException)
            {
            }

            // Register all the services and commands in the dotnet-dump (this) assembly
            ServiceManager.RegisterAssembly(typeof(Analyzer).Assembly);

            // Register all the services and commands in the SOS.Hosting assembly
            ServiceManager.RegisterAssembly(typeof(SOSHost).Assembly);

            // Register all the services and commands in the Microsoft.Diagnostics.ExtensionCommands assembly
            ServiceManager.RegisterAssembly(typeof(ClrMDHelper).Assembly);

            // Add the specially handled exit command
            _commandService.AddCommands(typeof(ExitCommand), (services) => new ExitCommand(_consoleService.Stop));

            // Display any extension assembly loads on console
            ServiceManager.NotifyExtensionLoad.Register((Assembly assembly) => _fileLoggingConsoleService.WriteLine($"Loading extension {assembly.Location}"));
            ServiceManager.NotifyExtensionLoadFailure.Register((Exception ex) => _fileLoggingConsoleService.WriteLine(ex.Message));

            // Load any extra extensions
            ServiceManager.LoadExtensions();

            // Loading extensions or adding service factories not allowed after this point.
            ServiceContainer serviceContainer = CreateServiceContainer();

            // Add all the global services to the global service container.
            serviceContainer.AddService<IConsoleService>(_fileLoggingConsoleService);
            serviceContainer.AddService<IConsoleFileLoggingService>(_fileLoggingConsoleService);
            serviceContainer.AddService<IDiagnosticLoggingService>(DiagnosticLoggingService.Instance);
            serviceContainer.AddService<ICommandService>(_commandService);
            serviceContainer.AddService<CommandService>(_commandService);
            serviceContainer.AddService<ISettingsService>(this);

            DumpTargetFactory dumpTargetFactory = new(this);
            serviceContainer.AddService<IDumpTargetFactory>(dumpTargetFactory);

            SymbolService symbolService = new(this);
            serviceContainer.AddService<ISymbolService>(symbolService);

            ContextService contextService = new(this);
            serviceContainer.AddService<IContextService>(contextService);

            try
            {
                ITarget target = dumpTargetFactory.OpenDump(dump_path.FullName);
                contextService.SetCurrentTarget(target);

                // Automatically enable symbol server support, default cache and search for symbols in the dump directory
                symbolService.AddSymbolServer(retryCount: 3);
                symbolService.AddCachePath(symbolService.DefaultSymbolCache);
                symbolService.AddDirectoryPath(Path.GetDirectoryName(dump_path.FullName));

                // Run the commands from the dotnet-dump command line. Any errors/exceptions from the
                // command execution will be displayed and dotnet-dump exited.
                if (command != null)
                {
                    foreach (string commandLine in command)
                    {
                        _commandService.Execute(commandLine, contextService.Services);
                        if (_consoleService.Shutdown)
                        {
                            break;
                        }
                    }
                }

                // Now start the REPL command loop if the console isn't redirected
                if (!_consoleService.Shutdown && (!Console.IsOutputRedirected || Console.IsInputRedirected))
                {
                    // Start interactive command line processing
                    _fileLoggingConsoleService.WriteLine("Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.");
                    _fileLoggingConsoleService.WriteLine("Type 'quit' or 'exit' to exit the session.");

                    _consoleService.Start((string prompt, string commandLine, CancellationToken cancellation) => {
                        _fileLoggingConsoleService.WriteLine("{0}{1}", prompt, commandLine);
                        _commandService.Execute(commandLine, contextService.Services);
                    });
                }
            }
            catch (Exception ex) when
                (ex is ClrDiagnosticsException
                 or DiagnosticsException
                 or FileNotFoundException
                 or DirectoryNotFoundException
                 or UnauthorizedAccessException
                 or PlatformNotSupportedException
                 or InvalidDataException
                 or InvalidOperationException
                 or NotSupportedException)
            {
                _fileLoggingConsoleService.WriteLineError($"{ex.Message}");
                return Task.FromResult(1);
            }
            finally
            {
                DestoryTargets();

                // Persist the current command history
                if (historyFileName != null)
                {
                    try
                    {
                        File.WriteAllLines(historyFileName, _consoleService.GetCommandHistory());
                    }
                    catch (Exception ex) when
                        (ex is IOException
                         or UnauthorizedAccessException
                         or NotSupportedException
                         or SecurityException)
                    {
                    }
                }

                // Send shutdown event on exit
                OnShutdownEvent.Fire();

                // Dispose of the global services
                serviceContainer.DisposeServices();
            }
            return Task.FromResult(0);
        }

        #region ISettingsService

        public bool DacSignatureVerificationEnabled { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? true : false;

        #endregion
    }
}
