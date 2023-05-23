// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.TestHelpers;
using Xunit.Abstractions;
using Xunit.Extensions;

public class SOSRunner : IDisposable
{
    /// <summary>
    /// What to use to generate the dump
    /// </summary>
    public enum DumpGenerator
    {
        NativeDebugger,
        CreateDump,
        DotNetDump,
    }

    /// <summary>
    /// Dump type
    /// </summary>
    public enum DumpType
    {
        Triage,
        Heap,
        Full
    }

    /// <summary>
    /// What action should the debugger do
    /// </summary>
    public enum DebuggerAction
    {
        Live,
        GenerateDump,
        LoadDump,
        LoadDumpWithDotNetDump,
    }

    /// <summary>
    /// Which debugger to use
    /// </summary>
    public enum NativeDebugger
    {
        Unknown,
        Cdb,
        Lldb,
        Gdb,
        DotNetDump,
    }

    /// <summary>
    /// SOS test runner config information
    /// </summary>
    public class TestInformation
    {
        private string _testName;
        private bool _testLive = true;
        private bool _testDump = true;
        private bool _testCrashReport = true;
        private DumpGenerator _dumpGenerator = DumpGenerator.CreateDump;
        private DumpType _dumpType = DumpType.Heap;
        private string _debuggeeDumpOutputRootDir;
        private string _debuggeeDumpInputRootDir;

        public TestConfiguration TestConfiguration { get; set; }

        public ITestOutputHelper OutputHelper { get; set; }

        public bool TestLive
        {
            // Don't test single file on Alpine. lldb 10.0 can't launch them.
            get { return _testLive && !(TestConfiguration.PublishSingleFile && OS.IsAlpine); }
            set { _testLive = value; }
        }

        public bool TestDump
        {
            get
            {
                return _testDump &&
                    // Only single file dumps on Windows
                    (!TestConfiguration.PublishSingleFile || OS.Kind == OSKind.Windows) &&
                    // Generate and test dumps if on OSX or Alpine only if the runtime is 6.0 or greater
                    (!(OS.Kind == OSKind.OSX || OS.IsAlpine) || TestConfiguration.RuntimeFrameworkVersionMajor > 5);
            }
            set { _testDump = value; }
        }

        public string TestName
        {
            get { return _testName ?? "SOS." + DebuggeeName; }
            set { _testName = value; }
        }

        public string DebuggeeName { get; set; }

        public string DebuggeeArguments { get; set; }

        public DumpGenerator DumpGenerator
        {
            get
            {
                DumpGenerator dumpGeneration = _dumpGenerator;
                if (dumpGeneration == DumpGenerator.CreateDump)
                {
                    if (!TestConfiguration.CreateDumpExists ||
                        TestConfiguration.PublishSingleFile ||
                        TestConfiguration.GenerateDumpWithLLDB() ||
                        TestConfiguration.GenerateDumpWithGDB())
                    {
                        dumpGeneration = DumpGenerator.NativeDebugger;
                    }
                }
                return dumpGeneration;
            }
            set { _dumpGenerator = value; }
        }

        public DumpType DumpType
        {
            get
            {
                // Currently neither cdb or dotnet-dump collect generates valid dumps on Windows for an single file app
                // Issue: https://github.com/dotnet/diagnostics/issues/2515
                return TestConfiguration.PublishSingleFile ? SOSRunner.DumpType.Full : _dumpType;
            }
            set { _dumpType = value; }
        }

        public bool UsePipeSync { get; set; }

        public bool DumpDiagnostics { get; set; } = true;

        public string DumpNameSuffix { get; set; }

        public bool EnableSOSLogging { get; set; } = true;

        public bool TestCrashReport
        {
            get { return _testCrashReport && DumpGenerator == DumpGenerator.CreateDump && OS.Kind != OSKind.Windows && TestConfiguration.RuntimeFrameworkVersionMajor >= 6; }
            set { _testCrashReport = value; }
        }

        public string DebuggeeDumpOutputRootDir
        {
            get { return _debuggeeDumpOutputRootDir ?? TestConfiguration.DebuggeeDumpOutputRootDir(); }
            set { _debuggeeDumpOutputRootDir = value; }
        }

        public string DebuggeeDumpInputRootDir
        {
            get { return _debuggeeDumpInputRootDir ?? TestConfiguration.DebuggeeDumpInputRootDir(); }
            set { _debuggeeDumpInputRootDir = value; }
        }

        public bool IsValid()
        {
            return TestConfiguration != null && OutputHelper != null && DebuggeeName != null;
        }
    }

    public const string HexValueRegEx = "[A-Fa-f0-9]+(`[A-Fa-f0-9]+)?";
    public const string DecValueRegEx = "[,0-9]+(`[,0-9]+)?";

    public NativeDebugger Debugger { get; private set; }

    public string DebuggerToString
    {
        get { return Debugger.ToString().ToUpperInvariant(); }
    }

    private readonly TestConfiguration _config;
    private readonly TestRunner.OutputHelper _outputHelper;
    private readonly Dictionary<string, string> _variables;
    private readonly ScriptLogger _scriptLogger;
    private readonly ProcessRunner _processRunner;
    private readonly DumpType? _dumpType;
    private string _lastCommandOutput;
    private string _previousCommandCapture;

    private SOSRunner(NativeDebugger debugger, TestConfiguration config, TestRunner.OutputHelper outputHelper, Dictionary<string, string> variables,
        ScriptLogger scriptLogger, ProcessRunner processRunner, DumpType? dumpType)
    {
        Debugger = debugger;
        _config = config;
        _outputHelper = outputHelper;
        _variables = variables;
        _scriptLogger = scriptLogger;
        _processRunner = processRunner;
        _dumpType = dumpType;
    }

    /// <summary>
    /// Run a debuggee and create a dump.
    /// </summary>
    /// <param name="information">test info</param>
    /// <returns>full dump name</returns>
    public static async Task<string> CreateDump(TestInformation information)
    {
        if (!information.IsValid())
        {
            throw new ArgumentException("Invalid TestInformation");
        }
        TestConfiguration config = information.TestConfiguration;
        DumpGenerator dumpGeneration = information.DumpGenerator;
        string dumpName = null;

        Directory.CreateDirectory(information.DebuggeeDumpOutputRootDir);

        if (dumpGeneration == DumpGenerator.NativeDebugger)
        {
            using SOSRunner runner = await SOSRunner.StartDebugger(information, DebuggerAction.GenerateDump);
            dumpName = runner.ReplaceVariables("%DUMP_NAME%");
            try
            {
                await runner.LoadSosExtension();

                string command = null;
                switch (runner.Debugger)
                {
                    case SOSRunner.NativeDebugger.Cdb:
                        await runner.ContinueExecution();
                        switch (information.DumpType)
                        {
                            case DumpType.Heap:
                                command = ".dump /o /mw %DUMP_NAME%";
                                break;
                            case DumpType.Triage:
                                command = ".dump /o /mshuRp %DUMP_NAME%";
                                break;
                            case DumpType.Full:
                                command = ".dump /o /ma %DUMP_NAME%";
                                break;
                        }
                        break;
                    case SOSRunner.NativeDebugger.Gdb:
                        command = "generate-core-file %DUMP_NAME%";
                        break;
                    default:
                        throw new Exception(runner.Debugger.ToString() + " does not support creating dumps");
                }

                await runner.RunCommand(command);
            }
            catch (Exception ex)
            {
                runner.WriteLine(ex.ToString());
                throw;
            }
            finally
            {
                await runner.QuitDebugger();
            }
        }
        else
        {
            TestRunner.OutputHelper outputHelper = null;
            NamedPipeServerStream pipeServer = null;
            string pipeName = null;
            try
            {
                // Setup the logging from the options in the config file
                outputHelper = TestRunner.ConfigureLogging(config, information.OutputHelper, information.TestName);

                // Restore and build the debuggee. The debuggee name is lower cased because the
                // source directory name has been lowercased by the build system.
                DebuggeeConfiguration debuggeeConfig = await DebuggeeCompiler.Execute(config, information.DebuggeeName, outputHelper);
                Dictionary<string, string> variables = GenerateVariables(information, debuggeeConfig, DebuggerAction.GenerateDump);
                dumpName = ReplaceVariables(variables, "%DUMP_NAME%");

                outputHelper.WriteLine("Starting {0}", information.TestName);
                outputHelper.WriteLine("{");

                // Get the full debuggee launch command line (includes the host if required)
                string exePath = debuggeeConfig.BinaryExePath;
                StringBuilder arguments = new();

                if (!string.IsNullOrWhiteSpace(config.HostExe))
                {
                    exePath = config.HostExe;
                    if (!string.IsNullOrWhiteSpace(config.HostArgs))
                    {
                        arguments.Append(config.HostArgs);
                        arguments.Append(' ');
                    }
                    arguments.Append(debuggeeConfig.BinaryExePath);
                }

                // Setup a pipe server for the debuggee to connect to sync when to take a dump
                if (information.UsePipeSync)
                {
                    int runnerId = Process.GetCurrentProcess().Id;
                    pipeName = $"SOSRunner.{runnerId}.{information.DebuggeeName}";
                    pipeServer = new NamedPipeServerStream(pipeName);
                    arguments.Append(' ');
                    arguments.Append(pipeName);
                }

                // Add any additional test specific arguments after the pipe name (if one).
                if (!string.IsNullOrWhiteSpace(information.DebuggeeArguments))
                {
                    arguments.Append(' ');
                    arguments.Append(information.DebuggeeArguments);
                }

                // Create the debuggee process runner
                ProcessRunner processRunner = new ProcessRunner(exePath, ReplaceVariables(variables, arguments.ToString())).
                    WithEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0").
                    WithEnvironmentVariable("DOTNET_ROOT", config.DotNetRoot).
                    WithRuntimeConfiguration("DbgEnableElfDumpOnMacOS", "1").
                    WithLog(new TestRunner.TestLogger(outputHelper.IndentedOutput)).
                    WithTimeout(TimeSpan.FromMinutes(10));

                if (dumpGeneration == DumpGenerator.CreateDump)
                {
                    // Run the debuggee with the createdump environment variables set to generate a coredump on unhandled exception
                    processRunner.
                        WithRuntimeConfiguration("DbgEnableMiniDump", "1").
                        WithRuntimeConfiguration("DbgMiniDumpName", dumpName);

                    if (information.DumpDiagnostics)
                    {
                        processRunner.WithRuntimeConfiguration("CreateDumpDiagnostics", "1");
                    }
                    if (information.TestCrashReport)
                    {
                        processRunner.WithRuntimeConfiguration("EnableCrashReport", "1");
                    }
                    // Windows createdump's triage MiniDumpWriteDump flags for .NET 5.0 are broken
                    // Disable testing triage dumps on 6.0 until the DAC signing issue is resolved - issue https://github.com/dotnet/diagnostics/issues/2542
                    // if (OS.Kind == OSKind.Windows && dumpType == DumpType.Triage && config.IsNETCore && config.RuntimeFrameworkVersionMajor < 6)
                    DumpType dumpType = information.DumpType;
                    if (OS.Kind == OSKind.Windows && dumpType == DumpType.Triage)
                    {
                        dumpType = DumpType.Heap;
                    }
                    switch (dumpType)
                    {
                        case DumpType.Heap:
                            processRunner.WithRuntimeConfiguration("DbgMiniDumpType", "2");
                            break;
                        case DumpType.Triage:
                            processRunner.WithRuntimeConfiguration("DbgMiniDumpType", "3");
                            break;
                        case DumpType.Full:
                            processRunner.WithRuntimeConfiguration("DbgMiniDumpType", "4");
                            break;
                    }
                }

                // Start the debuggee
                processRunner.Start();

                if (dumpGeneration == DumpGenerator.DotNetDump)
                {
                    ITestOutputHelper dotnetDumpOutputHelper = new IndentedTestOutputHelper(outputHelper, "        ");
                    try
                    {
                        if (string.IsNullOrWhiteSpace(config.DotNetDumpHost()) || string.IsNullOrWhiteSpace(config.DotNetDumpPath()))
                        {
                            throw new SkipTestException("dotnet-dump collect needs DotNetDumpHost and DotNetDumpPath config variables");
                        }

                        // Wait until the debuggee gets started. It needs time to spin up before generating the core dump.
                        if (pipeServer != null)
                        {
                            dotnetDumpOutputHelper.WriteLine("Waiting for connection on pipe {0}", pipeName);
                            CancellationTokenSource source = new(TimeSpan.FromMinutes(5));

                            // Wait for debuggee to connect/write to pipe or if the process exits on some other failure/abnormally
                            await Task.WhenAny(pipeServer.WaitForConnectionAsync(source.Token), processRunner.WaitForExit());
                        }

                        // Start dotnet-dump collect
                        DumpType dumpType = information.DumpType;
                        if (config.IsDesktop || config.RuntimeFrameworkVersionMajor < 6)
                        {
                            dumpType = DumpType.Full;
                        }
                        StringBuilder dotnetDumpArguments = new();
                        dotnetDumpArguments.Append(config.DotNetDumpPath());
                        dotnetDumpArguments.AppendFormat($" collect --process-id {processRunner.ProcessId} --output {dumpName} --type {dumpType}");
                        if (information.DumpDiagnostics)
                        {
                            dotnetDumpArguments.Append(" --diag");
                        }
                        ProcessRunner dotnetDumpRunner = new ProcessRunner(config.DotNetDumpHost(), ReplaceVariables(variables, dotnetDumpArguments.ToString())).
                            WithLog(new TestRunner.TestLogger(dotnetDumpOutputHelper)).
                            WithTimeout(TimeSpan.FromMinutes(10)).
                            WithExpectedExitCode(0);

                        dotnetDumpRunner.Start();

                        // Wait until dotnet-dump collect finishes generating the dump
                        await dotnetDumpRunner.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        // Log the exception
                        dotnetDumpOutputHelper.WriteLine(ex.ToString());
                    }
                    finally
                    {
                        dotnetDumpOutputHelper.WriteLine("}");

                        // Shutdown the debuggee
                        processRunner.Kill();
                    }
                }

                // Wait for the debuggee to finish
                await processRunner.WaitForExit();
            }
            catch (Exception ex)
            {
                // Log the exception
                outputHelper?.WriteLine(ex.ToString());
                throw;
            }
            finally
            {
                outputHelper?.WriteLine("}");
                outputHelper?.Dispose();
                pipeServer?.Dispose();
            }
        }
        return dumpName;
    }

    /// <summary>
    /// Start a debuggee under a native debugger returning a sos runner instance.
    /// </summary>
    /// <param name="information">test info</param>
    /// <param name="action">debugger action</param>
    /// <returns>sos runner instance</returns>
    public static async Task<SOSRunner> StartDebugger(TestInformation information, DebuggerAction action)
    {
        if (!information.IsValid())
        {
            throw new ArgumentException("Invalid TestInformation");
        }
        TestConfiguration config = information.TestConfiguration;
        TestRunner.OutputHelper outputHelper = null;
        SOSRunner sosRunner = null;

        try
        {
            // Setup the logging from the options in the config file
            outputHelper = TestRunner.ConfigureLogging(config, information.OutputHelper, information.TestName);
            string sosLogFile = information.EnableSOSLogging ? Path.Combine(config.LogDirPath, $"{information.TestName}.{config.LogSuffix}.soslog") : null;

            // Figure out which native debugger to use
            NativeDebugger debugger = GetNativeDebuggerToUse(config, action);

            // Restore and build the debuggee.
            DebuggeeConfiguration debuggeeConfig = await DebuggeeCompiler.Execute(config, information.DebuggeeName, outputHelper);

            outputHelper.WriteLine("SOSRunner processing {0}", information.TestName);
            outputHelper.WriteLine("{");

            Dictionary<string, string> variables = GenerateVariables(information, debuggeeConfig, action);
            ScriptLogger scriptLogger = new(outputHelper.IndentedOutput);

            // Make sure the dump file exists
            if (action is DebuggerAction.LoadDump or DebuggerAction.LoadDumpWithDotNetDump)
            {
                if (!variables.TryGetValue("%DUMP_NAME%", out string dumpName) || !File.Exists(dumpName))
                {
                    throw new FileNotFoundException($"Dump file does not exist: {dumpName ?? ""}");
                }
            }

            // Get the full debuggee launch command line (includes the host if required)
            StringBuilder debuggeeCommandLine = new();
            if (!string.IsNullOrWhiteSpace(config.HostExe))
            {
                debuggeeCommandLine.Append(config.HostExe);
                debuggeeCommandLine.Append(' ');
                if (!string.IsNullOrWhiteSpace(config.HostArgs))
                {
                    debuggeeCommandLine.Append(config.HostArgs);
                    debuggeeCommandLine.Append(' ');
                }
            }
            debuggeeCommandLine.Append(debuggeeConfig.BinaryExePath);
            if (!string.IsNullOrWhiteSpace(information.DebuggeeArguments))
            {
                debuggeeCommandLine.Append(' ');
                debuggeeCommandLine.Append(information.DebuggeeArguments);
            }

            // Get the native debugger path
            string debuggerPath = GetNativeDebuggerPath(debugger, config);
            if (string.IsNullOrWhiteSpace(debuggerPath) || !File.Exists(debuggerPath))
            {
                throw new FileNotFoundException($"Native debugger ({debugger}) path not set or does not exist: {debuggerPath}");
            }

            // Get the debugger arguments and commands to run initially
            List<string> initialCommands = new();
            StringBuilder arguments = new();

            switch (debugger)
            {
                case NativeDebugger.Cdb:
                    string helperExtension = config.CDBHelperExtension();
                    if (string.IsNullOrWhiteSpace(helperExtension) || !File.Exists(helperExtension))
                    {
                        throw new ArgumentException($"CDB helper script path not set or does not exist: {helperExtension}");
                    }
                    // Clear the default sympath (which puts a sym cache in the debugger binary directory in
                    // the .nuget cache) and set to just the directory containing the debuggee binaries.
                    arguments.AppendFormat(@" -y ""{0}""", debuggeeConfig.BinaryDirPath);
                    arguments.AppendFormat(@" -c "".load {0}""", helperExtension);

                    if (action == DebuggerAction.LoadDump)
                    {
                        arguments.Append(" -z %DUMP_NAME%");
                    }
                    else
                    {
                        arguments.AppendFormat(" -Gsins {0}", debuggeeCommandLine);

                        // disable stopping on integer divide-by-zero and integer overflow exceptions
                        initialCommands.Add("sxd dz");
                        initialCommands.Add("sxd iov");
                    }
                    initialCommands.Add(".extpath " + Path.GetDirectoryName(config.SOSPath()));

                    // Add the path to runtime so cdb/SOS can find DAC/DBI for triage dumps
                    if (information.DumpType == DumpType.Triage)
                    {
                        string runtimeSymbolsPath = config.RuntimeSymbolsPath;
                        if (runtimeSymbolsPath != null)
                        {
                            initialCommands.Add(".sympath+ " + runtimeSymbolsPath);
                        }
                    }
                    // Turn off warnings that can happen in the middle of a command's output
                    initialCommands.Add(".outmask- 0x244");
                    initialCommands.Add("!sym quiet");

                    // Turn on source/line numbers
                    initialCommands.Add(".lines");
                    break;

                case NativeDebugger.Lldb:
                    // Get the lldb python script file path necessary to capture the output of commands
                    // by printing a prompt after all the command output is printed.
                    string lldbHelperScript = config.LLDBHelperScript();
                    if (string.IsNullOrWhiteSpace(lldbHelperScript) || !File.Exists(lldbHelperScript))
                    {
                        throw new ArgumentException("LLDB helper script path not set or does not exist: " + lldbHelperScript);
                    }
                    arguments.Append(@"--no-lldbinit -o ""settings set target.disable-aslr false"" -o ""settings set interpreter.prompt-on-quit false""");
                    arguments.AppendFormat(@" -o ""command script import {0}"" -o ""version""", lldbHelperScript);

                    string debuggeeTarget = config.HostExe;
                    if (string.IsNullOrWhiteSpace(debuggeeTarget))
                    {
                        debuggeeTarget = debuggeeConfig.BinaryExePath;
                    }

                    // Load the dump or launch the debuggee process
                    if (action == DebuggerAction.LoadDump)
                    {
                        initialCommands.Add($@"target create --core ""%DUMP_NAME%"" ""{debuggeeTarget}""");
                    }
                    else
                    {
                        StringBuilder sb = new();
                        if (!string.IsNullOrWhiteSpace(config.HostArgs))
                        {
                            string[] args = ReplaceVariables(variables, config.HostArgs).Trim().Split(' ');
                            foreach (string arg in args)
                            {
                                sb.AppendFormat(@" ""{0}""", arg);
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(config.HostExe))
                        {
                            sb.AppendFormat(@" ""{0}""", debuggeeConfig.BinaryExePath);
                        }
                        if (!string.IsNullOrWhiteSpace(information.DebuggeeArguments))
                        {
                            string[] args = ReplaceVariables(variables, information.DebuggeeArguments).Trim().Split(' ');
                            foreach (string arg in args)
                            {
                                sb.AppendFormat(@" ""{0}""", arg);
                            }
                        }
                        initialCommands.Add($@"target create ""{debuggeeTarget}""");
                        string targetRunArgs = sb.ToString();
                        if (!string.IsNullOrWhiteSpace(targetRunArgs))
                        {
                            initialCommands.Add($"settings set -- target.run-args {targetRunArgs}");
                        }
                        initialCommands.Add("process launch -s");

                        // .NET Core 1.1 or less don't catch stack overflow and abort so need to catch SIGSEGV
                        if (config.StackOverflowSIGSEGV)
                        {
                            initialCommands.Add("process handle -s true -n true -p true SIGSEGV");
                        }
                        else
                        {
                            initialCommands.Add("process handle -s false -n false -p true SIGSEGV");
                        }
                        initialCommands.Add("process handle -s false -n false -p true SIGFPE");
                        initialCommands.Add("process handle -s true -n true -p true SIGABRT");
                    }
                    break;

                case NativeDebugger.Gdb:
                    if (action is DebuggerAction.LoadDump or DebuggerAction.LoadDumpWithDotNetDump)
                    {
                        throw new ArgumentException("GDB not meant for loading core dumps");
                    }

                    arguments.Append(@"--init-eval-command=""set prompt <END_COMMAND_OUTPUT>\n""");
                    arguments.AppendFormat(" --args {0}", debuggeeCommandLine);

                    // .NET Core 1.1 or less don't catch stack overflow and abort so need to catch SIGSEGV
                    if (config.StackOverflowSIGSEGV)
                    {
                        initialCommands.Add("handle SIGSEGV stop print");
                    }
                    else
                    {
                        initialCommands.Add("handle SIGSEGV nostop noprint");
                    }
                    initialCommands.Add("handle SIGFPE nostop noprint");
                    initialCommands.Add("handle SIGABRT stop print");
                    initialCommands.Add("set startup-with-shell off");
                    initialCommands.Add("set use-coredump-filter on");
                    initialCommands.Add("run");
                    break;

                case NativeDebugger.DotNetDump:
                    if (action != DebuggerAction.LoadDumpWithDotNetDump)
                    {
                        throw new ArgumentException($"{action} not supported for dotnet-dump testing");
                    }
                    if (string.IsNullOrWhiteSpace(config.DotNetDumpHost()))
                    {
                        throw new ArgumentException("No DotNetDumpHost in configuration");
                    }
                    // Add the path to runtime so dotnet-dump/SOS can find DAC/DBI for triage dumps
                    if (information.DumpType == DumpType.Triage)
                    {
                        string runtimeSymbolsPath = config.RuntimeSymbolsPath;
                        if (runtimeSymbolsPath != null)
                        {
                            initialCommands.Add("setclrpath " + runtimeSymbolsPath);
                        }
                    }
                    initialCommands.Add("setsymbolserver -directory %DEBUG_ROOT%");
                    arguments.Append(debuggerPath);
                    arguments.Append(@" analyze %DUMP_NAME%");
                    debuggerPath = config.DotNetDumpHost();
                    break;
            }


            // Create the native debugger process running
            ProcessRunner processRunner = new ProcessRunner(debuggerPath, ReplaceVariables(variables, arguments.ToString())).
                WithEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0").
                WithEnvironmentVariable("DOTNET_ROOT", config.DotNetRoot).
                WithLog(scriptLogger).
                WithTimeout(TimeSpan.FromMinutes(10));

            // Exit codes on Windows should always be 0, but not on Linux/OSX for the faulting debuggees.
            if (OS.Kind == OSKind.Windows)
            {
                processRunner.WithExpectedExitCode(0);
            }

            if (sosLogFile != null)
            {
                processRunner.WithEnvironmentVariable("DOTNET_ENABLED_SOS_LOGGING", sosLogFile);
            }

            // Disable W^E so that the bpmd command and the tests pass
            // Issue: https://github.com/dotnet/diagnostics/issues/3126
            processRunner.WithRuntimeConfiguration("EnableWriteXorExecute", "0");

            DumpType? dumpType = null;
            if (action is DebuggerAction.LoadDump or DebuggerAction.LoadDumpWithDotNetDump)
            {
                dumpType = information.DumpType;
            }

            // Create the sos runner instance
            sosRunner = new SOSRunner(debugger, config, outputHelper, variables, scriptLogger, processRunner, dumpType);

            // Start the native debugger
            processRunner.Start();

            // Set the coredump_filter flags on the gdb process so the coredump it
            // takes of the target process contains everything the tests need.
            if (debugger == NativeDebugger.Gdb)
            {
                initialCommands.Insert(0, string.Format("shell echo 0x3F > /proc/{0}/coredump_filter", processRunner.ProcessId));
            }

            // Execute the initial debugger commands
            await sosRunner.RunCommands(initialCommands);

            return sosRunner;
        }
        catch (Exception ex)
        {
            // Log the exception
            outputHelper?.WriteLine(ex.ToString());

            // The runner needs to kill the process and dispose of the file logger
            sosRunner?.Dispose();

            // The file logging output helper needs to be disposed to close the file
            outputHelper?.Dispose();
            throw;
        }
    }

    public async Task RunScript(string scriptRelativePath)
    {
        try
        {
            string scriptFile = Path.Combine(_config.ScriptRootDir, scriptRelativePath);
            if (!File.Exists(scriptFile))
            {
                throw new FileNotFoundException("Script file does not exist: " + scriptFile);
            }
            HashSet<string> enabledDefines = GetEnabledDefines();
            LogProcessingReproInfo(scriptFile, enabledDefines);
            string[] scriptLines = File.ReadAllLines(scriptFile);
            Dictionary<string, bool> activeDefines = new();
            bool isActiveDefineRegionEnabled = IsActiveDefineRegionEnabled(activeDefines, enabledDefines);
            int i = 0;
            try
            {
                for (; i < scriptLines.Length; i++)
                {
                    string line = scriptLines[i].TrimStart();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    {
                        continue;
                    }
                    else if (line.StartsWith("IFDEF:"))
                    {
                        string define = line.Substring("IFDEF:".Length).Trim();
                        activeDefines.Add(define, true);
                        isActiveDefineRegionEnabled = IsActiveDefineRegionEnabled(activeDefines, enabledDefines);
                    }
                    else if (line.StartsWith("!IFDEF:"))
                    {
                        string define = line.Substring("!IFDEF:".Length).Trim();
                        activeDefines.Add(define, false);
                        isActiveDefineRegionEnabled = IsActiveDefineRegionEnabled(activeDefines, enabledDefines);
                    }
                    else if (line.StartsWith("ENDIF:"))
                    {
                        string define = line.Substring("ENDIF:".Length).Trim();
                        if (!activeDefines.Last().Key.Equals(define))
                        {
                            throw new Exception("Mismatched IFDEF/ENDIF. IFDEF: " + activeDefines.Last().Key + " ENDIF: " + define);
                        }
                        activeDefines.Remove(define);
                        isActiveDefineRegionEnabled = IsActiveDefineRegionEnabled(activeDefines, enabledDefines);
                    }
                    else if (!isActiveDefineRegionEnabled)
                    {
                        WriteLine("    SKIPPING: {0}", line);
                        continue;
                    }
                    else if (line.StartsWith("LOADSOS"))
                    {
                        await LoadSosExtension();
                    }
                    else if (line.StartsWith("CONTINUE"))
                    {
                        await ContinueExecution();
                    }
                    else if (line.StartsWith("SOSCOMMAND:"))
                    {
                        string input = line.Substring("SOSCOMMAND:".Length).TrimStart();
                        if (!await RunSosCommand(input))
                        {
                            throw new Exception($"SOS command FAILED: {input}");
                        }
                    }
                    else if (line.StartsWith("EXTCOMMAND:"))
                    {
                        string input = line.Substring("EXTCOMMAND:".Length).TrimStart();
                        if (!await RunSosCommand(input, extensionCommand: true))
                        {
                            throw new Exception($"Extension command FAILED: {input}");
                        }
                    }
                    else if (line.StartsWith("COMMAND:"))
                    {
                        string input = line.Substring("COMMAND:".Length).TrimStart();
                        if (!await RunCommand(input))
                        {
                            throw new Exception($"Debugger command FAILED: {input}");
                        }
                    }
                    else if (line.StartsWith("VERIFY:"))
                    {
                        string verifyLine = line.Substring("VERIFY:".Length);
                        VerifyOutput(verifyLine, match: true);
                    }
                    else if (line.StartsWith("!VERIFY:"))
                    {
                        string verifyLine = line.Substring("!VERIFY:".Length);
                        VerifyOutput(verifyLine, match: false);
                    }
                    else
                    {
                        continue;
                    }
                }

                if (activeDefines.Count != 0)
                {
                    throw new Exception("Error unbalanced IFDEFs. " + activeDefines.First().Key + " has no ENDIF.");
                }

                await QuitDebugger();
            }
            catch (Exception)
            {
                WriteLine("SOSRunner error at " + scriptFile + ":" + (i + 1));
                WriteLine("Excerpt from " + scriptFile + ":");
                for (int j = Math.Max(0, i - 2); j < Math.Min(i + 3, scriptLines.Length); j++)
                {
                    WriteLine((j + 1).ToString().PadLeft(5) + " " + scriptLines[j]);
                }
                try
                {
                    _scriptLogger.FlushCurrentOutputAsError(_processRunner);
                    await RunCommand(".dump /o /ma %DUMP_NAME%");
                    await RunSosCommand("SOSStatus");
                }
                catch (Exception ex)
                {
                    WriteLine("Exception executing SOSStatus {0}", ex.ToString());
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            WriteLine(ex.ToString());
            throw;
        }
    }

    public async Task LoadSosExtension()
    {
        string runtimeSymbolsPath = _config.RuntimeSymbolsPath;
        string setHostRuntime = _config.SetHostRuntime();
        string setSymbolServer = _config.SetSymbolServer();
        string sosPath = _config.SOSPath();
        List<string> commands = new();
        bool isHostRuntimeNone = false;

        if (!string.IsNullOrEmpty(setHostRuntime))
        {
            switch (setHostRuntime)
            {
                case "-none":
                    isHostRuntimeNone = true;
                    break;
                case "-netfx":
                case "-netcore":
                case "-clear":
                    break;
                default:
                    setHostRuntime = TestConfiguration.MakeCanonicalPath(setHostRuntime);
                    break;
            }
        }
        switch (Debugger)
        {
            case NativeDebugger.Cdb:
                if (_config.IsDesktop)
                {
                    // Force the desktop sos to be loaded and then unload it.
                    if (!string.IsNullOrEmpty(runtimeSymbolsPath))
                    {
                        commands.Add($".cordll -lp {runtimeSymbolsPath}");
                    }
                    else
                    {
                        commands.Add(".cordll -l");
                    }
                }
                commands.Add(".unload sos");
                commands.Add($".load {sosPath}");
                commands.Add(".reload");
                commands.Add(".chain");
                if (!string.IsNullOrEmpty(setHostRuntime))
                {
                    commands.Add($"!SetHostRuntime {setHostRuntime}");
                }
                // If a single-file app or a triage dump, add the path to runtime so SOS can find DAC/DBI locally.
                if (_config.PublishSingleFile || (_dumpType.HasValue && _dumpType.Value == DumpType.Triage))
                {
                    if (!string.IsNullOrEmpty(runtimeSymbolsPath))
                    {
                        commands.Add($"!SetClrPath {runtimeSymbolsPath}");
                    }
                }
                if (!isHostRuntimeNone)
                {
                    // If single-file app, add the debuggee directory containing the PDBs and
                    // add the symbol server so SOS can find DAC/DBI for single file apps which
                    // may not have been built with the runtime pointed by RuntimeSymbolsPath
                    // since we use the arcade provided SDK (in .dotnet) to build them.
                    if (_config.PublishSingleFile)
                    {
                        string appRootDir = ReplaceVariables(_variables, "%DEBUG_ROOT%");
                        commands.Add($"!SetSymbolServer -ms -directory {appRootDir}");
                    }
                    if (!string.IsNullOrEmpty(setSymbolServer))
                    {
                        commands.Add($"!SetSymbolServer {setSymbolServer}");
                    }
                }
                break;
            case NativeDebugger.Lldb:
                commands.Add($"plugin load {sosPath}");
                if (!string.IsNullOrEmpty(setHostRuntime))
                {
                    commands.Add($"sethostruntime {setHostRuntime}");
                }
                // Disabled until https://github.com/dotnet/diagnostics/issues/3265 is fixed.
#if DISABLED
                // If a single-file app, add the path to runtime so SOS can find DAC/DBI locally.
                if (_config.PublishSingleFile)
                {
                    if (!string.IsNullOrEmpty(runtimeSymbolsPath))
                    {
                        commands.Add($"setclrpath {runtimeSymbolsPath}");
                    }
                }
#endif
                if (!isHostRuntimeNone)
                {
                    // If single-file app, add the debuggee directory containing the PDBs and
                    // add the symbol server so SOS can find DAC/DBI for single file apps which
                    // may not have been built with the runtime pointed by RuntimeSymbolsPath
                    // since we use the arcade provided SDK (in .dotnet) to build them.
                    if (_config.PublishSingleFile)
                    {
                        string appRootDir = ReplaceVariables(_variables, "%DEBUG_ROOT%");
                        commands.Add($"setsymbolserver -ms -directory {appRootDir}");
                    }
                    if (!string.IsNullOrEmpty(setSymbolServer))
                    {
                        commands.Add($"setsymbolserver {setSymbolServer}");
                    }
                }
                SwitchToExceptionThread();
                break;
            case NativeDebugger.Gdb:
                break;
            case NativeDebugger.DotNetDump:
                // If a single-file app, add the path to runtime so SOS can find DAC/DBI locally.
                if (_config.PublishSingleFile)
                {
                    if (!string.IsNullOrEmpty(runtimeSymbolsPath))
                    {
                        commands.Add($"setclrpath {runtimeSymbolsPath}");
                    }
                }
                if (!string.IsNullOrEmpty(setSymbolServer))
                {
                    commands.Add($"setsymbolserver {setSymbolServer}");
                }
                SwitchToExceptionThread();
                break;
            default:
                throw new Exception($"{DebuggerToString} cannot load sos extension");
        }
        await RunCommands(commands);

        // Helper function to switch to the thread with an exception
        void SwitchToExceptionThread()
        {
            // If dump session
            if (_dumpType.HasValue)
            {
                // lldb/dotnet-dump don't load dump with the initial thread set to one
                // with the exception. This SOS command looks for a thread with a managed
                // exception and set the current thread to it.
                commands.Add("clrthreads -managedexception");
            }
        }
    }

    public async Task ContinueExecution()
    {
        // If live session
        if (!_dumpType.HasValue)
        {
            string command = null;
            bool addPrefix = true;
            switch (Debugger)
            {
                case NativeDebugger.Cdb:
                    command = "g";
                    // Don't add the !runcommand prefix because it gets printed when cdb stops
                    // again because the helper extension used .pcmd to set a stop command.
                    addPrefix = false;
                    break;
                case NativeDebugger.Lldb:
                    command = "process continue";
                    break;
                case NativeDebugger.Gdb:
                    command = "continue";
                    break;
                case NativeDebugger.DotNetDump:
                    break;
            }
            if (command != null)
            {
                if (!await RunCommand(command, addPrefix))
                {
                    throw new Exception($"'{command}' FAILED");
                }
            }
        }
    }

    public async Task<bool> RunSosCommand(string command, bool extensionCommand = false)
    {
        switch (Debugger)
        {
            case NativeDebugger.Cdb:
                if (extensionCommand)
                {
                    command = "!sos " + command;
                }
                else
                {
                    command = "!" + command;
                }
                break;
            case NativeDebugger.Lldb:
                command = "sos " + command;
                break;
            case NativeDebugger.DotNetDump:
                if (extensionCommand)
                {
                    command = "sos " + command;
                }
                else
                {
                    int index = command.IndexOf(' ');
                    if (index != -1)
                    {
                        // lowercase just the command name not the rest of the command line
                        command = command.Substring(0, index).ToLowerInvariant() + command.Substring(index);
                    }
                    else
                    {
                        // it is only the command name
                        command = command.ToLowerInvariant();
                    }
                }
                break;
            default:
                throw new ArgumentException(DebuggerToString + " cannot execute sos command");
        }
        return await RunCommand(command);
    }

    public async Task RunCommands(IEnumerable<string> commands)
    {
        foreach (string command in commands)
        {
            if (!await RunCommand(command))
            {
                throw new Exception($"'{command}' FAILED");
            }
        }
    }

    public async Task<bool> RunCommand(string command, bool addPrefix = true)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Debugger command empty or null");
        }
        return await HandleCommand(command, addPrefix);
    }

    public async Task QuitDebugger()
    {
        if (await _scriptLogger.WaitForCommandPrompt())
        {
            string command = null;
            switch (Debugger)
            {
                case NativeDebugger.Cdb:
                case NativeDebugger.Gdb:
                    command = "q";
                    break;
                case NativeDebugger.Lldb:
                case NativeDebugger.DotNetDump:
                    command = "quit";
                    break;
            }
            _processRunner.StandardInputWriteLine(command);
            if (await _scriptLogger.WaitForCommandPrompt())
            {
                throw new Exception(DebuggerToString + " did not exit after quit command");
            }
        }
        await _processRunner.WaitForExit();
    }

    public void VerifyOutput(string verifyLine, bool match)
    {
        string regex = ReplaceVariables(verifyLine.TrimStart());

        if (_lastCommandOutput == null)
        {
            throw new Exception("VerifyOutput: no last command output or debugger exited unexpectedly: " + regex);
        }

        if (new Regex(regex, RegexOptions.Multiline).IsMatch(_lastCommandOutput) != match)
        {
            throw new Exception("Debugger output did not match the expression: " + regex);
        }
    }

    public static string GenerateDumpFileName(TestInformation information, string debuggeeName, DebuggerAction action)
    {
        string dumpRoot = action == DebuggerAction.GenerateDump ? information.DebuggeeDumpOutputRootDir : information.DebuggeeDumpInputRootDir;
        if (!string.IsNullOrEmpty(dumpRoot))
        {
            StringBuilder sb = new();
            sb.Append(information.TestName);
            sb.Append('.');
            sb.Append(information.DumpType.ToString());
            if (information.TestConfiguration.PublishSingleFile)
            {
                sb.Append(".SingleFile");
            }
            if (information.DumpNameSuffix != null)
            {
                sb.Append('.');
                sb.Append(information.DumpNameSuffix);
            }
            sb.Append(".dmp");
            return Path.Combine(dumpRoot, sb.ToString());
        }
        return null;
    }

    public void WriteLine(string message)
    {
        _outputHelper.IndentedOutput.WriteLine(message);
    }

    public void WriteLine(string format, params object[] args)
    {
        _outputHelper.IndentedOutput.WriteLine(format, args);
    }

    public void Dispose()
    {
        if (!_scriptLogger.HasProcessExited)
        {
            _processRunner.Kill();
        }
        _outputHelper.WriteLine("}");
        _outputHelper.Dispose();
    }

    private static NativeDebugger GetNativeDebuggerToUse(TestConfiguration config, DebuggerAction action)
    {
        switch (OS.Kind)
        {
            case OSKind.Windows:
                switch (action)
                {
                    case DebuggerAction.LoadDumpWithDotNetDump:
                        return NativeDebugger.DotNetDump;
                    default:
                        return NativeDebugger.Cdb;
                }

            case OSKind.Linux:
            case OSKind.OSX:
                switch (action)
                {
                    case DebuggerAction.GenerateDump:
                        return config.GenerateDumpWithLLDB() ? NativeDebugger.Lldb : NativeDebugger.Gdb;
                    case DebuggerAction.LoadDumpWithDotNetDump:
                        return NativeDebugger.DotNetDump;
                    default:
                        return NativeDebugger.Lldb;
                }

            default:
                throw new PlatformNotSupportedException(OS.Kind.ToString() + " not supported");
        }
    }

    private static string GetNativeDebuggerPath(NativeDebugger debugger, TestConfiguration config)
    {
        switch (debugger)
        {
            case NativeDebugger.Cdb:
                return config.CDBPath();

            case NativeDebugger.Lldb:
                return config.LLDBPath();

            case NativeDebugger.Gdb:
                return config.GDBPath();

            case NativeDebugger.DotNetDump:
                return config.DotNetDumpPath();
        }

        return null;
    }

    private async Task<bool> HandleCommand(string input, bool addPrefix)
    {
        if (!await _scriptLogger.WaitForCommandPrompt())
        {
            throw new Exception(string.Format("{0} exited unexpectedly executing '{1}'", DebuggerToString, input));
        }

        // The PREVPOUT convention is to write a command like this:
        // COMMAND: Some stuff <PREVPOUT> more stuff
        // The PREVPOUT tag will be replaced by whatever the last <POUT>
        // tag matched in a previous command. See below for the POUT rules.
        const string prevPoutTag = "<PREVPOUT>";
        const string poutTag = "<POUT>";
        if (input.Contains(prevPoutTag))
        {
            if (_previousCommandCapture == null)
            {
                throw new Exception(prevPoutTag + " in a COMMAND input requires a previous command with a " + poutTag + " that matched something");
            }
            input = input.Replace(prevPoutTag, _previousCommandCapture);
        }

        // The POUT convention is to write a command like this:
        // COMMAND: Some stuff <POUT>regex<POUT> more stuff
        // The regular expression identified by the POUT tags is applied to last command's output
        // and then the 1st capture group is substituted into this command in place of the POUT tagged region
        int firstPOUT = input.IndexOf(poutTag);
        if (firstPOUT != -1)
        {
            int secondPOUT = input.IndexOf(poutTag, firstPOUT + poutTag.Length);
            if (secondPOUT == -1)
            {
                throw new Exception("SOS script is missing closing " + poutTag + " tag");
            }
            else
            {
                if (_lastCommandOutput == null)
                {
                    throw new Exception(poutTag + " can't be used when there is no previous command output");
                }
                int startRegexIndex = firstPOUT + poutTag.Length;
                string poutRegex = input[startRegexIndex..secondPOUT];
                Match m = Regex.Match(_lastCommandOutput, ReplaceVariables(poutRegex), RegexOptions.Multiline);
                if (!m.Success)
                {
                    throw new Exception("The previous command output did not match the " + poutTag + " expression: " + poutRegex);
                }
                if (m.Groups.Count <= 1)
                {
                    throw new Exception("The " + poutTag + " regular expression must have a capture group");
                }
                string poutMatchResult = m.Groups[1].Value;
                _previousCommandCapture = poutMatchResult;
                input = input.Substring(0, firstPOUT) + poutMatchResult + input.Substring(secondPOUT + poutTag.Length);
            }
        }
        string command = ReplaceVariables(input);
        if (addPrefix)
        {
            command = ProcessCommand(command);
        }
        if (Debugger == NativeDebugger.Lldb)
        {
            // Back quotes need to be escaped so lldb passes them through
            command = command.Replace("`", "'`");
        }
        _processRunner.StandardInputWriteLine(command);

        ScriptLogger.CommandResult result = await _scriptLogger.WaitForCommandOutput();
        _lastCommandOutput = result.CommandOutput;
        if (Debugger == NativeDebugger.Cdb)
        {
            // Remove the cdb prompt because it interferes with script's regex's
            _lastCommandOutput = _lastCommandOutput?.Replace("0:000>", string.Empty);
        }
        return result.CommandSucceeded;
    }

    private string ProcessCommand(string command)
    {
        switch (Debugger)
        {
            case NativeDebugger.Cdb:
                command = string.Format("!runcommand {0}", command);
                break;

            case NativeDebugger.Lldb:
                command = string.Format("runcommand {0}", command);
                break;
        }
        return command;
    }

    private void LogProcessingReproInfo(string scriptFile, HashSet<string> enabledDefines)
    {
        WriteLine("    STARTING SCRIPT: {0}", scriptFile);
        foreach (KeyValuePair<string, string> kv in _variables)
        {
            WriteLine("    " + kv.Key + " => " + kv.Value);
        }
        foreach (string define in enabledDefines)
        {
            WriteLine("    " + define);
        }
    }

    private HashSet<string> GetEnabledDefines()
    {
        HashSet<string> defines = new()
        {
            DebuggerToString,
            OS.Kind.ToString().ToUpperInvariant(),
            _config.TestProduct.ToUpperInvariant(),
            _config.TargetArchitecture.ToUpperInvariant()
        };
        try
        {
            int major = _config.RuntimeFrameworkVersionMajor;
            defines.Add("MAJOR_RUNTIME_VERSION_" + major.ToString());
            if (major >= 3)
            {
                defines.Add("MAJOR_RUNTIME_VERSION_GE_3");
            }
            if (major >= 5)
            {
                defines.Add("MAJOR_RUNTIME_VERSION_GE_5");
            }
            if (major >= 6)
            {
                defines.Add("MAJOR_RUNTIME_VERSION_GE_6");
            }
            if (major >= 7)
            {
                defines.Add("MAJOR_RUNTIME_VERSION_GE_7");
            }
            if (major >= 8)
            {
                defines.Add("MAJOR_RUNTIME_VERSION_GE_8");
            }
        }
        catch (SkipTestException)
        {
        }
        if (_dumpType.HasValue)
        {
            switch (_dumpType.Value)
            {
                case DumpType.Triage:
                    defines.Add("TRIAGE_DUMP");
                    break;
                case DumpType.Heap:
                    defines.Add("HEAP_DUMP");
                    break;
                case DumpType.Full:
                    defines.Add("FULL_DUMP");
                    break;
            }
            defines.Add("DUMP");
        }
        else
        {
            defines.Add("LIVE");
        }
        if (_config.TargetArchitecture.Equals("x86") || _config.TargetArchitecture.Equals("arm"))
        {
            defines.Add("32BIT");
        }
        else if (_config.TargetArchitecture.Equals("x64") || _config.TargetArchitecture.Equals("arm64"))
        {
            defines.Add("64BIT");
        }
        else
        {
            throw new NotSupportedException("TargetArchitecture " + _config.TargetArchitecture + " not supported");
        }
        if (OS.IsAlpine)
        {
            defines.Add("ALPINE");
        }
        // This is a special "OR" of two conditions. Add this is easier than changing the parser to support "OR".
        if (_config.IsNETCore || Debugger == NativeDebugger.DotNetDump)
        {
            defines.Add("NETCORE_OR_DOTNETDUMP");
        }
        if (_config.PublishSingleFile)
        {
            defines.Add("SINGLE_FILE_APP");
            if (OS.Kind is OSKind.Linux or OSKind.OSX)
            {
                defines.Add("UNIX_SINGLE_FILE_APP");
            }
        }
        string setHostRuntime = _config.SetHostRuntime();
        if (!string.IsNullOrEmpty(setHostRuntime) && setHostRuntime == "-none")
        {
            defines.Add("HOST_RUNTIME_NONE");
        }
        return defines;
    }

    private static bool IsActiveDefineRegionEnabled(Dictionary<string, bool> activeDefines, HashSet<string> enabledDefines)
    {
        foreach (KeyValuePair<string, bool> activeDefine in activeDefines)
        {
            // If Value is true, then it should be defined. If false, then it should not be defined.
            if (enabledDefines.Contains(activeDefine.Key) != activeDefine.Value)
            {
                return false;
            }
        }
        return true;
    }

    private static Dictionary<string, string> GenerateVariables(TestInformation information, DebuggeeConfiguration debuggeeConfig, DebuggerAction action)
    {
        Dictionary<string, string> vars = new();
        string debuggeeExe = debuggeeConfig.BinaryExePath;
        string dumpFileName = GenerateDumpFileName(information, debuggeeExe, action);

        vars.Add("%DEBUGGEE_EXE%", debuggeeExe);
        if (dumpFileName != null)
        {
            vars.Add("%DUMP_NAME%", dumpFileName);
        }
        vars.Add("%DEBUG_ROOT%", debuggeeConfig.BinaryDirPath);
        vars.Add("%TEST_NAME%", information.TestName);
        vars.Add("%LOG_PATH%", information.TestConfiguration.LogDirPath);
        vars.Add("%LOG_SUFFIX%", information.TestConfiguration.LogSuffix);
        vars.Add("%SOS_PATH%", information.TestConfiguration.SOSPath());
        vars.Add("%DESKTOP_RUNTIME_PATH%", information.TestConfiguration.DesktopRuntimePath());

        // Can be used in an RegEx expression
        vars.Add("<DEBUGGEE_EXE>", debuggeeExe.Replace(@"\", @"\\"));
        vars.Add("<DEBUG_ROOT>", debuggeeConfig.BinaryDirPath.Replace(@"\", @"\\"));
        // On the desktop, the debuggee source is copied to this path but not built from this
        // path so this regex won't work for the desktop.
        vars.Add("<SOURCE_PATH>", debuggeeConfig.SourcePath.Replace(@"\", @"\\"));
        vars.Add("<HEXVAL>", HexValueRegEx);
        vars.Add("<DECVAL>", DecValueRegEx);

        return vars;
    }

    private string ReplaceVariables(string input)
    {
        return ReplaceVariables(_variables, input);
    }

    private static string ReplaceVariables(Dictionary<string, string> vars, string input)
    {
        string output = input;
        foreach (KeyValuePair<string, string> kv in vars)
        {
            output = output.Replace(kv.Key, kv.Value);
        }
        return output;
    }

    private class ScriptLogger : TestOutputProcessLogger
    {
        public struct CommandResult
        {
            public readonly string CommandOutput;       // Command output or null if process terminated.
            public readonly bool CommandSucceeded;      // If true, command succeeded.

            internal CommandResult(string commandOutput, bool commandSucceeded)
            {
                CommandOutput = commandOutput;
                CommandSucceeded = commandSucceeded;
            }
        }

        private readonly List<Task<CommandResult>> _taskQueue;
        private readonly StringBuilder _lineBuffer;
        private readonly StringBuilder _lastCommandOutput;
        private TaskCompletionSource<CommandResult> _taskSource;

        public bool HasProcessExited { get; private set; }

        public ScriptLogger(ITestOutputHelper output)
            : base(output)
        {
            lock (this)
            {
                _lineBuffer = new StringBuilder();
                _lastCommandOutput = new StringBuilder();
                _taskQueue = new List<Task<CommandResult>>();
                AddTask();
            }
        }

        private void AddTask()
        {
            _taskSource = new TaskCompletionSource<CommandResult>();
            _taskQueue.Add(_taskSource.Task);
        }

        public async Task<bool> WaitForCommandPrompt()
        {
            Task<CommandResult> currentTask = null;
            lock (this)
            {
                if (_taskQueue.Count == 0 || HasProcessExited)
                {
                    return false;
                }
                currentTask = _taskQueue[0];
                _taskQueue.RemoveAt(0);
            }
            return (await currentTask.ConfigureAwait(false)).CommandOutput != null;
        }

        public Task<CommandResult> WaitForCommandOutput()
        {
            Task<CommandResult> currentTask = null;
            lock (this)
            {
                Debug.Assert(_taskQueue.Count > 0);
                currentTask = _taskQueue[0];
            }
            return currentTask;
        }

        public override void Write(ProcessRunner runner, string data, ProcessStream stream)
        {
            lock (this)
            {
                base.Write(runner, data, stream);
                if (stream == ProcessStream.StandardOut)
                {
                    _lineBuffer.Append(data);
                }
            }
        }

        private static readonly string s_endCommandOutput = "<END_COMMAND_OUTPUT>";
        private static readonly string s_endCommandError = "<END_COMMAND_ERROR>";

        public override void WriteLine(ProcessRunner runner, string data, ProcessStream stream)
        {
            lock (this)
            {
                base.WriteLine(runner, data, stream);
                if (stream == ProcessStream.StandardOut)
                {
                    _lineBuffer.Append(data);
                    string lineBuffer = _lineBuffer.ToString();
                    _lineBuffer.Clear();

                    bool commandError = lineBuffer.EndsWith(s_endCommandError);
                    bool commandEnd = commandError || lineBuffer.EndsWith(s_endCommandOutput);
                    if (commandEnd)
                    {
                        FlushOutput();
                        _lastCommandOutput.AppendLine();
                        string lastCommandOutput = _lastCommandOutput.ToString();
                        _lastCommandOutput.Clear();
                        _taskSource.TrySetResult(new CommandResult(lastCommandOutput, !commandError));
                        AddTask();
                    }
                    else
                    {
                        _lastCommandOutput.AppendLine(lineBuffer);
                    }
                }
            }
        }

        public void FlushCurrentOutputAsError(ProcessRunner runner)
        {
            // TODO: Clean this up... It's acting as stdout from within
            // the runner, and it can act after the process exits and after
            // all output has been drained from the output streams. This output
            // would never get logged.
            WriteLine(runner, s_endCommandError, ProcessStream.StandardOut);
        }

        public override void ProcessExited(ProcessRunner runner)
        {
            lock (this)
            {
                base.ProcessExited(runner);
                FlushOutput();
                HasProcessExited = true;
                _taskSource.TrySetResult(new CommandResult(null, true));
            }
        }
    }
}

public static class TestConfigurationExtensions
{
    public static string CDBPath(this TestConfiguration config)
    {
        return TestConfiguration.MakeCanonicalExePath(config.GetValue("CDBPath"));
    }

    public static string CDBHelperExtension(this TestConfiguration config)
    {
        return TestConfiguration.MakeCanonicalPath(config.GetValue("CDBHelperExtension"));
    }

    public static string LLDBHelperScript(this TestConfiguration config)
    {
        return TestConfiguration.MakeCanonicalPath(config.GetValue("LLDBHelperScript"));
    }

    public static string LLDBPath(this TestConfiguration config)
    {
        string lldbPath = config.GetValue("LLDBPath");
        if (string.IsNullOrEmpty(lldbPath))
        {
            lldbPath = Environment.GetEnvironmentVariable("LLDB_PATH");
        }
        return TestConfiguration.MakeCanonicalPath(lldbPath);
    }

    public static string GDBPath(this TestConfiguration config)
    {
        string gdbPath = config.GetValue("GDBPath");
        if (string.IsNullOrEmpty(gdbPath))
        {
            gdbPath = Environment.GetEnvironmentVariable("GDB_PATH");
        }
        return TestConfiguration.MakeCanonicalPath(gdbPath);
    }

    public static string DotNetDumpHost(this TestConfiguration config)
    {
        string dotnetDumpHost = config.GetValue("DotNetDumpHost");
        return TestConfiguration.MakeCanonicalPath(dotnetDumpHost);
    }

    public static string DotNetDumpPath(this TestConfiguration config)
    {
        string dotnetDumpPath = config.GetValue("DotNetDumpPath");
        return TestConfiguration.MakeCanonicalPath(dotnetDumpPath);
    }

    public static string SOSPath(this TestConfiguration config)
    {
        return TestConfiguration.MakeCanonicalPath(config.GetValue("SOSPath"));
    }

    public static string SetHostRuntime(this TestConfiguration config)
    {
        return config.GetValue("SetHostRuntime");
    }

    public static string SetSymbolServer(this TestConfiguration config)
    {
        return config.GetValue("SetSymbolServer");
    }

    public static string DesktopRuntimePath(this TestConfiguration config)
    {
        return TestConfiguration.MakeCanonicalPath(config.GetValue("DesktopRuntimePath"));
    }

    public static bool GenerateDumpWithLLDB(this TestConfiguration config)
    {
        return config.GetValue("GenerateDumpWithLLDB")?.ToLowerInvariant() == "true";
    }

    public static bool GenerateDumpWithGDB(this TestConfiguration config)
    {
        return config.GetValue("GenerateDumpWithGDB")?.ToLowerInvariant() == "true";
    }

    public static string DebuggeeDumpInputRootDir(this TestConfiguration config)
    {
        return TestConfiguration.MakeCanonicalPath(config.GetValue("DebuggeeDumpInputRootDir"));
    }

    public static string DebuggeeDumpOutputRootDir(this TestConfiguration config)
    {
        return TestConfiguration.MakeCanonicalPath(config.GetValue("DebuggeeDumpOutputRootDir"));
    }
}
