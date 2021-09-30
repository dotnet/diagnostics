// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.ExtensionCommands;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using SOS.Hosting;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Security;

namespace Microsoft.Diagnostics.Tools.Dump
{
    public class Analyzer : IHost
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ConsoleProvider _consoleProvider;
        private readonly CommandProcessor _commandProcessor;
        private readonly SymbolService _symbolService;
        private readonly ContextService _contextService;
        private int _targetIdFactory;
        private Target _target;

        public Analyzer()
        {
            LoggingCommand.Initialize();

            _serviceProvider = new ServiceProvider();
            _consoleProvider = new ConsoleProvider();
            _commandProcessor = new CommandProcessor();
            _symbolService = new SymbolService(this);
            _contextService = new ContextService(this);

            _serviceProvider.AddService<IHost>(this);
            _serviceProvider.AddService<IConsoleService>(_consoleProvider);
            _serviceProvider.AddService<ICommandService>(_commandProcessor);
            _serviceProvider.AddService<ISymbolService>(_symbolService);
            _serviceProvider.AddService<IContextService>(_contextService);
            _serviceProvider.AddServiceFactory<SOSLibrary>(() => SOSLibrary.Create(this));

            _contextService.ServiceProvider.AddServiceFactory<ClrMDHelper>(() => {
                ClrRuntime clrRuntime = _contextService.Services.GetService<ClrRuntime>();
                return clrRuntime != null ? new ClrMDHelper(clrRuntime) : null;
            });

            _commandProcessor.AddCommands(new Assembly[] { typeof(Analyzer).Assembly });
            _commandProcessor.AddCommands(new Assembly[] { typeof(ClrMDHelper).Assembly });
            _commandProcessor.AddCommands(new Assembly[] { typeof(SOSHost).Assembly });
            _commandProcessor.AddCommands(typeof(HelpCommand), (services) => new HelpCommand(_commandProcessor, services));
            _commandProcessor.AddCommands(typeof(ExitCommand), (services) => new ExitCommand(_consoleProvider.Stop));
        }

        public Task<int> Analyze(FileInfo dump_path, string[] command)
        {
            _consoleProvider.WriteLine($"Loading core dump: {dump_path} ...");

            // Attempt to load the persisted command history
            string dotnetHome;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                dotnetHome = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), ".dotnet");
            }
            else { 
                dotnetHome = Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".dotnet");
            }
            string historyFileName = Path.Combine(dotnetHome, "dotnet-dump.history");
            try
            {
                string[] history = File.ReadAllLines(historyFileName);
                _consoleProvider.AddCommandHistory(history);
            }
            catch (Exception ex) when 
                (ex is IOException || 
                 ex is UnauthorizedAccessException || 
                 ex is NotSupportedException || 
                 ex is SecurityException)
            {
            }

            try
            { 
                using DataTarget dataTarget = DataTarget.LoadDump(dump_path.FullName);

                OSPlatform targetPlatform = dataTarget.DataReader.TargetPlatform;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || dataTarget.DataReader.EnumerateModules().Any((module) => Path.GetExtension(module.FileName) == ".dylib")) {
                    targetPlatform = OSPlatform.OSX;
                }
                _target = new TargetFromDataReader(dataTarget.DataReader, targetPlatform, this, _targetIdFactory++, dump_path.FullName);
                _contextService.SetCurrentTarget(_target);

                _target.ServiceProvider.AddServiceFactory<SOSHost>(() => new SOSHost(_contextService.Services));

                // Automatically enable symbol server support, default cache and search for symbols in the dump directory
                _symbolService.AddSymbolServer(msdl: true, symweb: false, symbolServerPath: null, authToken: null, timeoutInMinutes: 0);
                _symbolService.AddCachePath(_symbolService.DefaultSymbolCache);
                _symbolService.AddDirectoryPath(Path.GetDirectoryName(dump_path.FullName));

                // Run the commands from the dotnet-dump command line
                if (command != null)
                {
                    foreach (string cmd in command)
                    {
                        _commandProcessor.Execute(cmd, _contextService.Services);
                        if (_consoleProvider.Shutdown) {
                            break;
                        }
                    }
                }
                if (!_consoleProvider.Shutdown && (!Console.IsOutputRedirected || Console.IsInputRedirected))
                {
                    // Start interactive command line processing
                    _consoleProvider.WriteLine("Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.");
                    _consoleProvider.WriteLine("Type 'quit' or 'exit' to exit the session.");

                    _consoleProvider.Start((string commandLine, CancellationToken cancellation) => {
                        _commandProcessor.Execute(commandLine, _contextService.Services);
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
                _consoleProvider.WriteLine(OutputType.Error, $"{ex.Message}");
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
                    File.WriteAllLines(historyFileName, _consoleProvider.GetCommandHistory());
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
            if (target == null) {
                throw new ArgumentNullException(nameof(target));
            }
            if (target == _target)
            {
                _target = null;
                _contextService.ClearCurrentTarget();
                if (target is IDisposable disposable) {
                    disposable.Dispose();
                }
            }
        }

        #endregion
    }
}
