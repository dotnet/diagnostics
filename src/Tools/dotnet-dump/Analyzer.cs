// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using SOS.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Dump
{
    public class Analyzer : IHost
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ConsoleService _consoleService;
        private readonly FileLoggingConsoleService _fileLoggingConsoleService;
        private readonly CommandService _commandService;
        private readonly SymbolService _symbolService;
        private readonly ContextService _contextService;
        private int _targetIdFactory;
        private Target _target;

        public Analyzer()
        {
            DiagnosticLoggingService.Initialize();

            _serviceProvider = new ServiceProvider();
            _consoleService = new ConsoleService();
            _fileLoggingConsoleService  = new FileLoggingConsoleService(_consoleService);
            _commandService = new CommandService();
            _symbolService = new SymbolService(this);
            _contextService = new ContextService(this);
            DiagnosticLoggingService.Instance.SetConsole(_fileLoggingConsoleService, _fileLoggingConsoleService);

            _serviceProvider.AddService<IHost>(this);
            _serviceProvider.AddService<IConsoleService>(_fileLoggingConsoleService);
            _serviceProvider.AddService<IConsoleFileLoggingService>(_fileLoggingConsoleService);
            _serviceProvider.AddService<IDiagnosticLoggingService>(DiagnosticLoggingService.Instance);
            _serviceProvider.AddService<ICommandService>(_commandService);
            _serviceProvider.AddService<ISymbolService>(_symbolService);
            _serviceProvider.AddService<IContextService>(_contextService);
            _serviceProvider.AddServiceFactory<SOSLibrary>(() => SOSLibrary.Create(this));

            _contextService.ServiceProvider.AddServiceFactory<ClrMDHelper>(() => {
                ClrRuntime clrRuntime = _contextService.Services.GetService<ClrRuntime>();
                return clrRuntime != null ? new ClrMDHelper(clrRuntime) : null;
            });

            _commandService.AddCommands(new Assembly[] { typeof(Analyzer).Assembly });
            _commandService.AddCommands(new Assembly[] { typeof(ClrMDHelper).Assembly });
            _commandService.AddCommands(new Assembly[] { typeof(SOSHost).Assembly });
            _commandService.AddCommands(typeof(HelpCommand), (services) => new HelpCommand(_commandService, services));
            _commandService.AddCommands(typeof(ExitCommand), (services) => new ExitCommand(_consoleService.Stop));
            _commandService.AddCommands(typeof(SOSCommand), (services) => new SOSCommand(_commandService, services));
        }

        public Task<int> Analyze(FileInfo dump_path, string[] command)
        {
            _fileLoggingConsoleService.WriteLine($"Loading core dump: {dump_path} ...");

            // Attempt to load the persisted command history
            string dotnetHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
            string historyFileName = Path.Combine(dotnetHome, "dotnet-dump.history");
            try
            {
                string[] history = File.ReadAllLines(historyFileName);
                _consoleService.AddCommandHistory(history);
            }
            catch (Exception ex) when
                (ex is IOException ||
                 ex is UnauthorizedAccessException ||
                 ex is NotSupportedException ||
                 ex is SecurityException)
            {
            }

            // Load any extra extensions
            LoadExtensions();

            try
            {
                using DataTarget dataTarget = DataTarget.LoadDump(dump_path.FullName);

                OSPlatform targetPlatform = dataTarget.DataReader.TargetPlatform;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || dataTarget.DataReader.EnumerateModules().Any((module) => Path.GetExtension(module.FileName) == ".dylib"))
                {
                    targetPlatform = OSPlatform.OSX;
                }
                _target = new TargetFromDataReader(dataTarget.DataReader, targetPlatform, this, _targetIdFactory++, dump_path.FullName);
                _contextService.SetCurrentTarget(_target);

                _target.ServiceProvider.AddServiceFactory<SOSHost>(() => new SOSHost(_contextService.Services));

                // Automatically enable symbol server support, default cache and search for symbols in the dump directory
                _symbolService.AddSymbolServer(msdl: true, symweb: false, retryCount: 3);
                _symbolService.AddCachePath(_symbolService.DefaultSymbolCache);
                _symbolService.AddDirectoryPath(Path.GetDirectoryName(dump_path.FullName));

                // Run the commands from the dotnet-dump command line
                if (command != null)
                {
                    foreach (string cmd in command)
                    {
                        _commandService.Execute(cmd, _contextService.Services);
                        if (_consoleService.Shutdown)
                        {
                            break;
                        }
                    }
                }
                if (!_consoleService.Shutdown && (!Console.IsOutputRedirected || Console.IsInputRedirected))
                {
                    // Start interactive command line processing
                    _fileLoggingConsoleService.WriteLine("Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.");
                    _fileLoggingConsoleService.WriteLine("Type 'quit' or 'exit' to exit the session.");

                    _consoleService.Start((string prompt, string commandLine, CancellationToken cancellation) =>
                    {
                        _fileLoggingConsoleService.WriteLine("{0}{1}", prompt, commandLine);
                        _commandService.Execute(commandLine, _contextService.Services);
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
                _fileLoggingConsoleService.WriteError($"{ex.Message}");
                return Task.FromResult(1);
            }
            finally
            {
                if (_target != null)
                {
                    DestroyTarget(_target);
                }
                // Persist the current command history
                try
                {
                    File.WriteAllLines(historyFileName, _consoleService.GetCommandHistory());
                }
                catch (Exception ex) when
                    (ex is IOException ||
                     ex is UnauthorizedAccessException ||
                     ex is NotSupportedException ||
                     ex is SecurityException)
                {
                }
                // Send shutdown event on exit
                OnShutdownEvent.Fire();
            }
            return Task.FromResult(0);
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        HostType IHost.HostType => HostType.DotnetDump;

        IServiceProvider IHost.Services => _serviceProvider;

        IEnumerable<ITarget> IHost.EnumerateTargets() => _target != null ? new ITarget[] { _target } : Array.Empty<ITarget>();

        public void DestroyTarget(ITarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (target == _target)
            {
                _target = null;
                _contextService.ClearCurrentTarget();
                if (target is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        #endregion

        /// <summary>
        /// Load any extra extensions in the search path
        /// </summary>
        /// <param name="commandService">Used to add the commands</param>
        private void LoadExtensions()
        {
            string diagnosticExtensions = Environment.GetEnvironmentVariable("DOTNET_DIAGNOSTIC_EXTENSIONS");
            if (!string.IsNullOrEmpty(diagnosticExtensions))
            {
                string[] paths = diagnosticExtensions.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string extensionPath in paths)
                {
                    LoadExtension(extensionPath);
                }
            }
        }

        /// <summary>
        /// Load any extra extensions in the search path
        /// </summary>
        /// <param name="commandService">Used to add the commands</param>
        /// <param name="extensionPath">Extension assembly path</param>
        private void LoadExtension(string extensionPath)
        {
            Assembly assembly = null;
            try
            {
                assembly = Assembly.LoadFrom(extensionPath);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is BadImageFormatException || ex is System.Security.SecurityException)
            {
                _fileLoggingConsoleService.WriteLineError($"Extension load {extensionPath} FAILED {ex.Message}");
            }
            if (assembly is not null)
            {
                _commandService.AddCommands(assembly);
                _fileLoggingConsoleService.WriteLine($"Extension loaded {extensionPath}");
            }
        }
    }
}
