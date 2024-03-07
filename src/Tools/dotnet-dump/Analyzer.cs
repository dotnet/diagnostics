// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public class Analyzer : IHost
    {
        private readonly ServiceManager _serviceManager;
        private readonly ConsoleService _consoleService;
        private readonly FileLoggingConsoleService _fileLoggingConsoleService;
        private readonly CommandService _commandService;
        private readonly List<ITarget> _targets = new();
        private ServiceContainer _serviceContainer;
        private int _targetIdFactory;

        public Analyzer()
        {
            DiagnosticLoggingService.Initialize();

            _serviceManager = new ServiceManager();
            _consoleService = new ConsoleService();
            _fileLoggingConsoleService = new FileLoggingConsoleService(_consoleService);
            DiagnosticLoggingService.Instance.SetConsole(_fileLoggingConsoleService, _fileLoggingConsoleService);

            _commandService = new CommandService();
            _serviceManager.NotifyExtensionLoad.Register(_commandService.AddCommands);

            // Add and remove targets from the host
            OnTargetCreate.Register((target) => {
                _targets.Add(target);
                target.OnDestroyEvent.Register(() => {
                    _targets.Remove(target);
                });
            });
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

            // Register all the services and commands in the Microsoft.Diagnostics.DebugServices.Implementation assembly
            _serviceManager.RegisterAssembly(typeof(Target).Assembly);

            // Register all the services and commands in the dotnet-dump (this) assembly
            _serviceManager.RegisterAssembly(typeof(Analyzer).Assembly);

            // Register all the services and commands in the SOS.Hosting assembly
            _serviceManager.RegisterAssembly(typeof(SOSHost).Assembly);

            // Register all the services and commands in the Microsoft.Diagnostics.ExtensionCommands assembly
            _serviceManager.RegisterAssembly(typeof(ClrMDHelper).Assembly);

            // Add the specially handled exit command
            _commandService.AddCommands(typeof(ExitCommand), (services) => new ExitCommand(_consoleService.Stop));

            // Display any extension assembly loads on console
            _serviceManager.NotifyExtensionLoad.Register((Assembly assembly) => _fileLoggingConsoleService.WriteLine($"Loading extension {assembly.Location}"));
            _serviceManager.NotifyExtensionLoadFailure.Register((Exception ex) => _fileLoggingConsoleService.WriteLine(ex.Message));

            // Load any extra extensions
            _serviceManager.LoadExtensions();

            // Loading extensions or adding service factories not allowed after this point.
            _serviceManager.FinalizeServices();

            // Add all the global services to the global service container
            _serviceContainer = _serviceManager.CreateServiceContainer(ServiceScope.Global, parent: null);
            _serviceContainer.AddService<IServiceManager>(_serviceManager);
            _serviceContainer.AddService<IHost>(this);
            _serviceContainer.AddService<IConsoleService>(_fileLoggingConsoleService);
            _serviceContainer.AddService<IConsoleFileLoggingService>(_fileLoggingConsoleService);
            _serviceContainer.AddService<IDiagnosticLoggingService>(DiagnosticLoggingService.Instance);
            _serviceContainer.AddService<ICommandService>(_commandService);
            _serviceContainer.AddService<CommandService>(_commandService);

            SymbolService symbolService = new(this);
            _serviceContainer.AddService<ISymbolService>(symbolService);

            ContextService contextService = new(this);
            _serviceContainer.AddService<IContextService>(contextService);

            try
            {
                using DataTarget dataTarget = DataTarget.LoadDump(dump_path.FullName);

                OSPlatform targetPlatform = dataTarget.DataReader.TargetPlatform;
                if (targetPlatform != OSPlatform.OSX &&
                    (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                     dataTarget.DataReader.EnumerateModules().Any((module) => Path.GetExtension(module.FileName) == ".dylib")))
                {
                    targetPlatform = OSPlatform.OSX;
                }
                TargetFromDataReader target = new(dataTarget.DataReader, targetPlatform, this, _targetIdFactory++, dump_path.FullName);
                contextService.SetCurrentTarget(target);

                // Automatically enable symbol server support, default cache and search for symbols in the dump directory
                symbolService.AddSymbolServer(msdl: true, symweb: false, retryCount: 3);
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
                foreach (ITarget target in _targets.ToArray())
                {
                    target.Destroy();
                }
                _targets.Clear();

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
                _serviceContainer.DisposeServices();
            }
            return Task.FromResult(0);
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        public IServiceEvent<ITarget> OnTargetCreate { get; } = new ServiceEvent<ITarget>();

        public HostType HostType => HostType.DotnetDump;

        public IServiceProvider Services => _serviceContainer;

        public IEnumerable<ITarget> EnumerateTargets() => _targets.ToArray();

        #endregion
    }
}
