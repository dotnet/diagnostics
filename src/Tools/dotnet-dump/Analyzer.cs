// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Repl;
using Microsoft.Diagnostics.Runtime;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using SOS;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Dump
{
    public class Analyzer
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ConsoleProvider _consoleProvider;
        private readonly CommandProcessor _commandProcessor;
        private bool _isDesktop;
        private string _dacFilePath;

        /// <summary>
        /// Enable the assembly resolver to get the right SOS.NETCore version (the one
        /// in the same directory as this assembly).
        /// </summary>
        static Analyzer()
        {
            AssemblyResolver.Enable();
        }

        public Analyzer()
        {
            _serviceProvider = new ServiceProvider();
            _consoleProvider = new ConsoleProvider();
            _commandProcessor = new CommandProcessor(_serviceProvider, _consoleProvider, new Assembly[] { typeof(Analyzer).Assembly });
        }

        public async Task<int> Analyze(FileInfo dump_path, string[] command)
        {
            _consoleProvider.WriteLine($"Loading core dump: {dump_path} ...");

            try
            { 
                DataTarget target = null;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    target = DataTarget.LoadCoreDump(dump_path.FullName);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    try
                    {
                        target = DataTarget.LoadCoreDump(dump_path.FullName);
                    }
                    catch (InvalidDataException)
                    {
                        // This condition occurs when we try to load a Windows dump as a Elf core dump.
                    }

                    if (target == null)
                    {
                        target = DataTarget.LoadCrashDump(dump_path.FullName, CrashDumpReader.ClrMD);
                    }
                }
                else {
                    throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                }

                using (target)
                {
                    _consoleProvider.WriteLine("Ready to process analysis commands. Type 'help' to list available commands or 'help [command]' to get detailed help on a command.");
                    _consoleProvider.WriteLine("Type 'quit' or 'exit' to exit the session.");

                    // Add all the services needed by commands and other services
                    AddServices(target);

                    // Automatically enable symbol server support
                    SymbolReader.InitializeSymbolStore(
                        logging: false, 
                        msdl: true,
                        symweb: false,
                        tempDirectory: null,
                        symbolServerPath: null,
                        timeoutInMinutes: 0,
                        symbolCachePath: null,
                        symbolDirectoryPath: null,
                        windowsSymbolPath: null);

                    // Run the commands from the dotnet-dump command line
                    if (command != null)
                    {
                        foreach (string cmd in command)
                        {
                            await _commandProcessor.Parse(cmd);

                            if (_consoleProvider.Shutdown)
                                break;
                        }
                    }

                    // Start interactive command line processing
                    var analyzeContext = _serviceProvider.GetService<AnalyzeContext>();
                    await _consoleProvider.Start(async (string commandLine, CancellationToken cancellation) => {
                        analyzeContext.CancellationToken = cancellation;
                        await _commandProcessor.Parse(commandLine);
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
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Add all the services needed by commands
        /// </summary>
        private void AddServices(DataTarget target)
        {
            _serviceProvider.AddService(target);
            _serviceProvider.AddService<IConsoleService>(_consoleProvider);
            _serviceProvider.AddService(_commandProcessor);
            _serviceProvider.AddServiceFactory(typeof(IHelpBuilder), _commandProcessor.CreateHelpBuilder);

            // Create common analyze context for commands
            var analyzeContext = new AnalyzeContext() {
                CurrentThreadId = unchecked((int)target.DataReader.EnumerateAllThreads().FirstOrDefault())
            };
            _serviceProvider.AddService(analyzeContext);

            // Add the register, memory, SOSHost and ClrRuntime services
            var registerService = new RegisterService(target);
            _serviceProvider.AddService(registerService);

            var memoryService = new MemoryService(target.DataReader);
            _serviceProvider.AddService(memoryService);

            _serviceProvider.AddServiceFactory(typeof(ClrRuntime), () => CreateRuntime(target));

            _serviceProvider.AddServiceFactory(typeof(SOSHost), () => {
                var sosHost = new SOSHost(_serviceProvider);
                sosHost.InitializeSOSHost(SymbolReader.TempDirectory, _isDesktop, _dacFilePath, dbiFilePath: null);
                return sosHost;
            });
        }

        /// <summary>
        /// ClrRuntime service factory
        /// </summary>
        private ClrRuntime CreateRuntime(DataTarget target)
        {
            ClrInfo clrInfo = null;

            // First check if there is a .NET Core runtime loaded
            foreach (ClrInfo clr in target.ClrVersions)
            {
                if (clr.Flavor == ClrFlavor.Core)
                {
                    clrInfo = clr;
                    break;
                }
            }
            // If no .NET Core runtime, then check for desktop runtime
            if (clrInfo == null)
            {
                foreach (ClrInfo clr in target.ClrVersions)
                {
                    if (clr.Flavor == ClrFlavor.Desktop)
                    {
                        clrInfo = clr;
                        break;
                    }
                }
            }
            if (clrInfo == null) {
                throw new InvalidOperationException("No CLR runtime is present");
            }
            ClrRuntime runtime;
            string dacFilePath = GetDacFile(clrInfo);
            try
            {
                // Ignore the DAC version mismatch that can happen on Linux because the clrmd ELF dump 
                // reader returns 0.0.0.0 for the runtime module that the DAC is matched against. This
                // will be fixed in clrmd 2.0 but not 1.1.
                runtime = clrInfo.CreateRuntime(dacFilePath, ignoreMismatch: clrInfo.ModuleInfo.BuildId != null);
            }
            catch (DllNotFoundException ex)
            {
                // This is a workaround for the Microsoft SDK docker images. Can fail when clrmd uses libdl.so 
                // to create a symlink to and load the DAC module.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    throw new DllNotFoundException("Problem initializing CLRMD. Try installing libc6-dev (apt-get install libc6-dev) to work around this problem.", ex);
                }
                else
                {
                    throw;
                }
            }
            return runtime;
        }

        private string GetPlatformDacFileName(string dacFileName)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return dacFileName.Replace("libmscordaccore.so", "mscordaccore.dll");
            }
            return dacFileName;
        }

        private string GetDacFile(ClrInfo clrInfo)
        {
            if (_dacFilePath == null)
            {            
                Debug.Assert(!string.IsNullOrEmpty(clrInfo.DacInfo.FileName));
                var analyzeContext = _serviceProvider.GetService<AnalyzeContext>();
                string dacFilePath = null;
                if (!string.IsNullOrEmpty(analyzeContext.RuntimeModuleDirectory))
                {
                    dacFilePath = Path.Combine(analyzeContext.RuntimeModuleDirectory, GetPlatformDacFileName(clrInfo.DacInfo.FileName));
                    if (File.Exists(dacFilePath))
                    {
                        _dacFilePath = dacFilePath;
                    }
                }
                if (_dacFilePath == null)
                {
                    dacFilePath = GetPlatformDacFileName(clrInfo.LocalMatchingDac);
                    if (!string.IsNullOrEmpty(dacFilePath) && File.Exists(dacFilePath))
                    {
                        _dacFilePath = dacFilePath;
                    }
                    else if (SymbolReader.IsSymbolStoreEnabled())
                    {
                        string dacFileName = Path.GetFileName(dacFilePath ?? GetPlatformDacFileName(clrInfo.DacInfo.FileName));
                        if (dacFileName != null)
                        {
                            SymbolStoreKey key = null;

                            if (clrInfo.ModuleInfo.BuildId != null)
                            {
                                IEnumerable<SymbolStoreKey> keys = ELFFileKeyGenerator.GetKeys(
                                    KeyTypeFlags.DacDbiKeys, clrInfo.ModuleInfo.FileName, clrInfo.ModuleInfo.BuildId, symbolFile: false, symbolFileName: null);

                                key = keys.SingleOrDefault((k) => Path.GetFileName(k.FullPathName) == dacFileName);
                            }
                            else
                            {
                                // Use the coreclr.dll's id (timestamp/filesize) to download the the dac module.
                                key = PEFileKeyGenerator.GetKey(dacFileName, clrInfo.ModuleInfo.TimeStamp, clrInfo.ModuleInfo.FileSize);
                            }

                            if (key != null)
                            {
                                // Now download the DAC module from the symbol server
                                _dacFilePath = SymbolReader.GetSymbolFile(key);
                            }
                        }
                    }

                    if (_dacFilePath == null)
                    {
                        throw new FileNotFoundException($"Could not find matching DAC for this runtime: {clrInfo.ModuleInfo.FileName}");
                    }
                }
                _isDesktop = clrInfo.Flavor == ClrFlavor.Desktop;
            }
            return _dacFilePath;
        }
    }
}
