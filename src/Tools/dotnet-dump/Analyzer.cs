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

namespace Microsoft.Diagnostics.Tools.Dump
{
    public class Analyzer : IHost
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ConsoleProvider _consoleProvider;
        private readonly CommandProcessor _commandProcessor;
        private readonly SymbolService _symbolService;
        private Target _target;

        public Analyzer()
        {
            _serviceProvider = new ServiceProvider();
            _consoleProvider = new ConsoleProvider();
            _commandProcessor = new CommandProcessor();
            _symbolService = new SymbolService(this);

            _serviceProvider.AddService<IHost>(this);
            _serviceProvider.AddService<IConsoleService>(_consoleProvider);
            _serviceProvider.AddService<ICommandService>(_commandProcessor);
            _serviceProvider.AddService<ISymbolService>(_symbolService);

            _commandProcessor.AddCommands(new Assembly[] { typeof(Analyzer).Assembly });
            _commandProcessor.AddCommands(new Assembly[] { typeof(ClrMDHelper).Assembly });
            _commandProcessor.AddCommands(new Assembly[] { typeof(SOSHost).Assembly });
            _commandProcessor.AddCommands(typeof(HelpCommand), (services) => new HelpCommand(_commandProcessor, services));
            _commandProcessor.AddCommands(typeof(ExitCommand), (services) => new ExitCommand(_consoleProvider.Stop));
        }

        public Task<int> Analyze(FileInfo dump_path, string[] command)
        {
            _consoleProvider.WriteLine($"Loading core dump: {dump_path} ...");

            try
            { 
                using DataTarget dataTarget = DataTarget.LoadDump(dump_path.FullName);

                OSPlatform targetPlatform = dataTarget.DataReader.TargetPlatform;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    targetPlatform = OSPlatform.OSX;
                }
                _target = new TargetFromDataReader(dataTarget.DataReader, targetPlatform, this, dump_path.FullName);

                _target.ServiceProvider.AddServiceFactory<SOSHost>(() => {
                    var sosHost = new SOSHost(_target);
                    sosHost.InitializeSOSHost();
                    return sosHost;
                });

                // Automatically enable symbol server support
                _symbolService.AddSymbolServer(msdl: true, symweb: false, symbolServerPath: null, authToken: null, timeoutInMinutes: 0);
                _symbolService.AddCachePath(_symbolService.DefaultSymbolCache);

                // Run the commands from the dotnet-dump command line
                if (command != null)
                {
                    foreach (string cmd in command)
                    {
                        Parse(cmd);

                        if (_consoleProvider.Shutdown) {
                            break;
                        }
                    }
                }
                if (!_consoleProvider.Shutdown)
                {
                    // Start interactive command line processing
                    _consoleProvider.WriteLine("Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.");
                    _consoleProvider.WriteLine("Type 'quit' or 'exit' to exit the session.");

                    _consoleProvider.Start((string commandLine, CancellationToken cancellation) => {
                        Parse(commandLine);
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
                    _target.Close();
                    _target = null;
                }
                // Send shutdown event on exit
                OnShutdownEvent.Fire();
            }
            return Task.FromResult(0);
        }

        private void Parse(string commandLine)
        {
            // If there is no target, then provide just the global services
            ServiceProvider services = _serviceProvider;
            if (_target != null)
            {
                // Create a per command invocation service provider. These services may change between each command invocation.
                services = new ServiceProvider(_target.Services);

                // Add the current thread if any
                services.AddServiceFactory<IThread>(() => {
                    IThreadService threadService = _target.Services.GetService<IThreadService>();
                    if (threadService != null && threadService.CurrentThreadId.HasValue) {
                        return threadService.GetThreadFromId(threadService.CurrentThreadId.Value);
                    }
                    return null;
                });

                // Add the current runtime and related services
                var runtimeService = _target.Services.GetService<IRuntimeService>();
                if (runtimeService != null)
                {
                    services.AddServiceFactory<IRuntime>(() => runtimeService.CurrentRuntime);
                    services.AddServiceFactory<ClrRuntime>(() => services.GetService<IRuntime>()?.Services.GetService<ClrRuntime>());
                    services.AddServiceFactory<ClrMDHelper>(() => new ClrMDHelper(services.GetService<ClrRuntime>()));
                }
            }
            _commandProcessor.Execute(commandLine, services);
        }

        #region IHost

        public IServiceEvent OnShutdownEvent { get; } = new ServiceEvent();

        HostType IHost.HostType => HostType.DotnetDump;

        IServiceProvider IHost.Services => _serviceProvider;

        IEnumerable<ITarget> IHost.EnumerateTargets() => _target != null ? new ITarget[] { _target } : Array.Empty<ITarget>();

        ITarget IHost.CurrentTarget => _target;

        void IHost.SetCurrentTarget(int targetid) => throw new NotImplementedException();

        #endregion
    }
}
