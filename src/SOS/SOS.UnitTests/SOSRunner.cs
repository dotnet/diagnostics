// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.TestHelpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

public class SOSRunner : IDisposable
{
    readonly TestConfiguration _config;
    readonly TestRunner.OutputHelper _outputHelper;
    readonly Dictionary<string, string> _variables;
    readonly ScriptLogger _scriptLogger;
    readonly ProcessRunner _processRunner;
    readonly bool _isDump;

    string _lastCommandOutput;
    string _previousCommandCapture;

    public enum Options
    {
        None,
        GenerateDump,
        LoadDump,
        LoadDumpWithDotNetDump,
    }

    public enum NativeDebugger
    {
        Unknown,
        Cdb,
        Lldb,
        Gdb,
        DotNetDump,
    }

    public const string HexValueRegEx = "[A-Fa-f0-9]+(`[A-Fa-f0-9]+)?";
    public const string DecValueRegEx = "[0-9]+(`[0-9]+)?";

    public NativeDebugger Debugger { get; private set; }

    public string DebuggerToString
    {
        get { return Debugger.ToString().ToUpperInvariant(); }
    }

    private SOSRunner(NativeDebugger debugger, TestConfiguration config, TestRunner.OutputHelper outputHelper, 
        Dictionary<string, string> variables, ScriptLogger scriptLogger, ProcessRunner processRunner, bool isDump)
    {
        Debugger = debugger;
        _config = config;
        _outputHelper = outputHelper;
        _variables = variables;
        _scriptLogger = scriptLogger;
        _processRunner = processRunner;
        _isDump = isDump;
    }

    /// <summary>
    /// Run a debuggee and create a dump.
    /// </summary>
    /// <param name="config">test configuration</param>
    /// <param name="output">output instance</param>
    /// <param name="testName">name of test</param>
    /// <param name="debuggeeName">debuggee name</param>
    /// <param name="debuggeeArguments">optional args to pass to debuggee</param>
    /// <param name="useCreateDump">if true, use "createdump" to generate core dump</param>
    public static async Task CreateDump(TestConfiguration config, ITestOutputHelper output, string testName, string debuggeeName, 
        string debuggeeArguments = null, bool useCreateDump = true)
    {
        Directory.CreateDirectory(config.DebuggeeDumpOutputRootDir());

        if (!config.CreateDumpExists || !useCreateDump || config.GenerateDumpWithLLDB() || config.GenerateDumpWithGDB())
        {
            using (SOSRunner runner = await SOSRunner.StartDebugger(config, output, testName, debuggeeName, debuggeeArguments, Options.GenerateDump))
            {
                try
                {
                    await runner.LoadSosExtension();

                    string command = null;
                    switch (runner.Debugger)
                    {
                        case SOSRunner.NativeDebugger.Cdb:
                            await runner.ContinueExecution();
                            // On desktop create triage dump. On .NET Core, create full dump.
                            command = config.IsDesktop ? ".dump /o /mshuRp %DUMP_NAME%" : ".dump /o /ma %DUMP_NAME%";
                            break;
                        case SOSRunner.NativeDebugger.Gdb:
                            command = "generate-core-file %DUMP_NAME%";
                            break;
                        case SOSRunner.NativeDebugger.Lldb:
                            await runner.ContinueExecution();
                            command = "sos CreateDump %DUMP_NAME%";
                            break;
                        default:
                            throw new Exception(runner.Debugger.ToString() + " does not support creating dumps");
                    }

                    await runner.RunCommand(command);
                    await runner.QuitDebugger();
                }
                catch (Exception ex)
                {
                    runner.WriteLine(ex.ToString());
                    throw;
                }
            }
        }
        else
        {
            TestRunner.OutputHelper outputHelper = null;
            try
            {
                // Setup the logging from the options in the config file
                outputHelper = TestRunner.ConfigureLogging(config, output, testName);

                // Restore and build the debuggee. The debuggee name is lower cased because the 
                // source directory name has been lowercased by the build system.
                DebuggeeConfiguration debuggeeConfig = await DebuggeeCompiler.Execute(config, debuggeeName, outputHelper);

                outputHelper.WriteLine("Starting {0}", testName);
                outputHelper.WriteLine("{");

                // Get the full debuggee launch command line (includes the host if required)
                string exePath = debuggeeConfig.BinaryExePath;
                var arguments = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(config.HostExe))
                {
                    exePath = config.HostExe;
                    if (!string.IsNullOrWhiteSpace(config.HostArgs))
                    {
                        arguments.Append(config.HostArgs);
                        arguments.Append(" ");
                    }
                    arguments.Append(debuggeeConfig.BinaryExePath);
                }
                if (!string.IsNullOrWhiteSpace(debuggeeArguments))
                {
                    arguments.Append(" ");
                    arguments.Append(debuggeeArguments);
                }

                // Run the debuggee with the createdump environment variables set to generate a coredump on unhandled exception
                var testLogger = new TestRunner.TestLogger(outputHelper.IndentedOutput);
                var variables = GenerateVariables(config, debuggeeConfig, Options.GenerateDump);
                ProcessRunner processRunner = new ProcessRunner(exePath, ReplaceVariables(variables, arguments.ToString())).
                    WithLog(testLogger).
                    WithTimeout(TimeSpan.FromMinutes(5)).
                    WithEnvironmentVariable("COMPlus_DbgEnableMiniDump", "1").
                    WithEnvironmentVariable("COMPlus_DbgMiniDumpName", ReplaceVariables(variables,"%DUMP_NAME%"));

                processRunner.Start();

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
            }
        }
    }

    /// <summary>
    /// Start a debuggee under a native debugger returning a sos runner instance.
    /// </summary>
    /// <param name="config">test configuration</param>
    /// <param name="output">output instance</param>
    /// <param name="testName">name of test</param>
    /// <param name="debuggeeName">debuggee name</param>
    /// <param name="debuggeeArguments">optional args to pass to debuggee</param>
    /// <param name="options">dump options</param>
    /// <returns>sos runner instance</returns>
    public static async Task<SOSRunner> StartDebugger(TestConfiguration config, ITestOutputHelper output, string testName, string debuggeeName, 
        string debuggeeArguments = null, Options options = Options.None)
    {
        TestRunner.OutputHelper outputHelper = null;
        SOSRunner sosRunner = null;

        // Figure out which native debugger to use
        NativeDebugger debugger = GetNativeDebuggerToUse(config, options);

        try
        {
            // Setup the logging from the options in the config file
            outputHelper = TestRunner.ConfigureLogging(config, output, testName);

            // Restore and build the debuggee.
            DebuggeeConfiguration debuggeeConfig = await DebuggeeCompiler.Execute(config, debuggeeName, outputHelper);

            outputHelper.WriteLine("SOSRunner processing {0}", testName);
            outputHelper.WriteLine("{");

            var variables = GenerateVariables(config, debuggeeConfig, options);
            var scriptLogger = new ScriptLogger(debugger, outputHelper.IndentedOutput);

            if (options == Options.LoadDump || options == Options.LoadDumpWithDotNetDump)
            {
                if (!variables.TryGetValue("%DUMP_NAME%", out string dumpName) || !File.Exists(dumpName))
                {
                    throw new FileNotFoundException($"Dump file does not exist: {dumpName ?? ""}");
                }
            }

            // Get the full debuggee launch command line (includes the host if required)
            var debuggeeCommandLine = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(config.HostExe))
            {
                debuggeeCommandLine.Append(config.HostExe);
                debuggeeCommandLine.Append(" ");
                if (!string.IsNullOrWhiteSpace(config.HostArgs))
                {
                    debuggeeCommandLine.Append(config.HostArgs);
                    debuggeeCommandLine.Append(" ");
                }
            }
            debuggeeCommandLine.Append(debuggeeConfig.BinaryExePath);
            if (!string.IsNullOrWhiteSpace(debuggeeArguments))
            {
                debuggeeCommandLine.Append(" ");
                debuggeeCommandLine.Append(debuggeeArguments);
            }

            // Get the native debugger path
            string debuggerPath = GetNativeDebuggerPath(debugger, config);
            if (string.IsNullOrWhiteSpace(debuggerPath) || !File.Exists(debuggerPath))
            {
                throw new FileNotFoundException($"Native debugger path not set or does not exist: {debuggerPath}");
            }

            // Get the debugger arguments and commands to run initially
            List<string> initialCommands = new List<string>();
            var arguments = new StringBuilder();

            switch (debugger)
            {
                case NativeDebugger.Cdb:
                    string helperExtension = config.CDBHelperExtension();
                    if (string.IsNullOrWhiteSpace(helperExtension) || !File.Exists(helperExtension))
                    {
                        throw new ArgumentException($"CDB helper script path not set or does not exist: {helperExtension}");
                    }
                    arguments.AppendFormat(@"-c "".load {0}""", helperExtension);

                    if (options == Options.LoadDump)
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
                    initialCommands.Add(".sympath %DEBUG_ROOT%");
                    initialCommands.Add(".extpath " + Path.GetDirectoryName(config.SOSPath()));

                    // Add the path to runtime so cdb/sos can find mscordbi.
                    string runtimeSymbolsPath = config.RuntimeSymbolsPath;
                    if (runtimeSymbolsPath != null)
                    {
                        initialCommands.Add(".sympath+ " + runtimeSymbolsPath);
                    }
                    // Turn off warnings that can happen in the middle of a command's output
                    initialCommands.Add(".outmask- 4");
                    break;
                case NativeDebugger.Lldb:
                    // Get the lldb python script file path necessary to capture the output of commands
                    // by printing a prompt after all the command output is printed.
                    string lldbHelperScript = config.LLDBHelperScript();
                    if (string.IsNullOrWhiteSpace(lldbHelperScript) || !File.Exists(lldbHelperScript))
                    {
                        throw new ArgumentException("LLDB helper script path not set or does not exist: " + lldbHelperScript);
                    }
                    arguments.AppendFormat(@"--no-lldbinit -o ""settings set interpreter.prompt-on-quit false"" -o ""command script import {0}"" -o ""version""", lldbHelperScript);

                    // Load the dump or launch the debuggee process
                    if (options == Options.LoadDump)
                    {
                        initialCommands.Add($@"target create --core ""%DUMP_NAME%"" ""{config.HostExe}""");
                    }
                    else
                    {
                        var sb = new StringBuilder("settings set -- target.run-args");
                        if (!string.IsNullOrWhiteSpace(config.HostArgs))
                        {
                            string[] args = ReplaceVariables(variables, config.HostArgs).Trim().Split(' ');
                            foreach (string arg in args)
                            {
                                sb.AppendFormat(@" ""{0}""", arg);
                            }
                        }
                        sb.AppendFormat(@" ""{0}""", debuggeeConfig.BinaryExePath);
                        if (!string.IsNullOrWhiteSpace(debuggeeArguments))
                        {
                            string[] args = ReplaceVariables(variables, debuggeeArguments).Trim().Split(' ');
                            foreach (string arg in args)
                            {
                                sb.AppendFormat(@" ""{0}""", arg);
                            }
                        }
                        initialCommands.Add($@"target create ""{config.HostExe}""");
                        initialCommands.Add(sb.ToString());
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
                    if (options == Options.LoadDump || options == Options.LoadDumpWithDotNetDump)
                    {
                        throw new ArgumentException("GDB not meant for loading core dumps");
                    }
                    arguments.AppendFormat("--args {0}", debuggeeCommandLine);

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
                    if (options != Options.LoadDumpWithDotNetDump)
                    {
                        throw new ArgumentException($"{options} not supported for dotnet-dump testing");
                    }
                    if (string.IsNullOrWhiteSpace(config.HostExe))
                    {
                        throw new ArgumentException("No HostExe in configuration");
                    }
                    arguments.Append(debuggerPath);
                    arguments.Append(@" analyze %DUMP_NAME%");
                    debuggerPath = config.HostExe;
                    break;
            }

            // Create the native debugger process running
            ProcessRunner processRunner = new ProcessRunner(debuggerPath, ReplaceVariables(variables, arguments.ToString())).
                WithLog(scriptLogger).
                WithTimeout(TimeSpan.FromMinutes(10));

            // Create the sos runner instance
            sosRunner = new SOSRunner(debugger, config, outputHelper, variables, scriptLogger, processRunner, options == Options.LoadDump || options == Options.LoadDumpWithDotNetDump);

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
        string scriptFile = Path.Combine(_config.ScriptRootDir, scriptRelativePath);
        if (!File.Exists(scriptFile))
        {
            throw new FileNotFoundException("Script file does not exist: " + scriptFile);
        }
        HashSet<string> enabledDefines = GetEnabledDefines();
        LogProcessingReproInfo(scriptFile, enabledDefines);
        string[] scriptLines = File.ReadAllLines(scriptFile);
        Dictionary<string, bool> activeDefines = new Dictionary<string, bool>();
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
                    string define = line.Substring("IFDEF:".Length);
                    activeDefines.Add(define, true);
                    isActiveDefineRegionEnabled = IsActiveDefineRegionEnabled(activeDefines, enabledDefines);
                }
                else if (line.StartsWith("!IFDEF:"))
                {
                    string define = line.Substring("!IFDEF:".Length);
                    activeDefines.Add(define, false);
                    isActiveDefineRegionEnabled = IsActiveDefineRegionEnabled(activeDefines, enabledDefines);
                }
                else if (line.StartsWith("ENDIF:"))
                {
                    string define = line.Substring("ENDIF:".Length);
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
                    VerifyOutput(verifyLine);
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
        catch (Exception e)
        {
            WriteLine("SOSRunner error at " + scriptFile + ":" + (i + 1));
            WriteLine("Excerpt from " + scriptFile + ":");
            for (int j = Math.Max(0, i - 2); j < Math.Min(i + 3, scriptLines.Length); j++)
            {
                WriteLine((j + 1).ToString().PadLeft(5) + " " + scriptLines[j]);
            }
            WriteLine(e.ToString());
            throw;
        }
    }

    public async Task LoadSosExtension()
    {
        string sosHostRuntime = _config.SOSHostRuntime();
        string sosPath = _config.SOSPath();
        List<string> commands = new List<string>();

        switch (Debugger)
        {
            case NativeDebugger.Cdb:
                commands.Add($".load {sosPath}");
                commands.Add(".lines");
                commands.Add(".reload");
                if (sosHostRuntime != null)
                {
                    commands.Add($"!SetHostRuntime {sosHostRuntime}");
                }
                break;
            case NativeDebugger.Lldb:
                commands.Add($"plugin load {sosPath}");
                if (sosHostRuntime != null)
                {
                    commands.Add($"sos SetHostRuntime {sosHostRuntime}");
                }
                SwitchToExceptionThread();
                break;
            case NativeDebugger.Gdb:
                break;
            case NativeDebugger.DotNetDump:
                SwitchToExceptionThread();
                break;
            default:
                throw new Exception($"{DebuggerToString} cannot load sos extension");
        }
        await RunCommands(commands);

        // Helper function to switch to the thread with an exception
        void SwitchToExceptionThread()
        {
            if (_isDump)
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

    public async Task<bool> RunSosCommand(string command)
    {
        switch (Debugger)
        {
            case NativeDebugger.Cdb:
                command = "!" + command;
                break;
            case NativeDebugger.Lldb:
                command = "sos " + command;
                break;
            case NativeDebugger.DotNetDump:
                int index = command.IndexOf(' ');
                if (index != -1) {
                    // lowercase just the command name not the rest of the command line
                    command = command.Substring(0, index).ToLowerInvariant() + command.Substring(index);
                }
                else {
                    // it is only the command name
                    command = command.ToLowerInvariant();
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

    public void VerifyOutput(string verifyLine)
    {
        string regex = ReplaceVariables(verifyLine.TrimStart());

        if (_lastCommandOutput == null)
        {
            throw new Exception("VerifyOutput: no last command output or debugger exited unexpectedly: " + regex);
        }
        if (!new Regex(regex, RegexOptions.Multiline).IsMatch(_lastCommandOutput))
        {
            throw new Exception("Debugger output did not match the expression: " + regex);
        }
    }

    public static string GenerateDumpFileName(TestConfiguration config, string debuggeeName, Options options)
    {
        string dumpRoot = options == Options.GenerateDump ? config.DebuggeeDumpOutputRootDir() : config.DebuggeeDumpInputRootDir();
        if (dumpRoot != null)
        {
            return Path.Combine(dumpRoot, Path.GetFileNameWithoutExtension(debuggeeName) + ".dmp");
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
        _processRunner.WaitForExit().GetAwaiter().GetResult();

        _outputHelper.WriteLine("}");
        _outputHelper.Dispose();
    }

    public static bool IsAlpine()
    {
        if (OS.Kind == OSKind.Linux)
        {
            try
            {
                string ostype = File.ReadAllText("/etc/os-release");
                return ostype.Contains("ID=alpine");
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is IOException)
            {
            }
        }
        return false;
    }

    private static NativeDebugger GetNativeDebuggerToUse(TestConfiguration config, Options options)
    {
        switch (OS.Kind)
        {
            case OSKind.Windows:
                switch (options) {
                    case Options.LoadDumpWithDotNetDump:
                        return NativeDebugger.DotNetDump;
                    default:
                        return NativeDebugger.Cdb;
                }

            case OSKind.Linux:
            case OSKind.OSX:
                switch (options) {
                    case Options.GenerateDump: 
                        return config.GenerateDumpWithLLDB() ? NativeDebugger.Lldb : NativeDebugger.Gdb;
                    case Options.LoadDumpWithDotNetDump:
                        return NativeDebugger.DotNetDump;
                    default:
                        return NativeDebugger.Lldb;
                }

            default:
                throw new Exception(OS.Kind.ToString() + " not supported");
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
                string poutRegex = input.Substring(startRegexIndex, secondPOUT - startRegexIndex);
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
            command = _scriptLogger.ProcessCommand(command);
        }
        _processRunner.StandardInputWriteLine(command);

        ScriptLogger.CommandResult result = await _scriptLogger.WaitForCommandOutput();
        _lastCommandOutput = result.CommandOutput;

        return result.CommandSucceeded;
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
        HashSet<string> defines = new HashSet<string>
        {
            DebuggerToString,
            OS.Kind.ToString().ToUpperInvariant(),
            _config.TestProduct.ToUpperInvariant(),
            _config.TargetArchitecture.ToUpperInvariant(),
            "MAJOR_RUNTIME_VERSION_" + _config.RuntimeFrameworkVersionMajor.ToString()
        };
        if (_isDump)
        {
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
        if (IsAlpine())
        {
            defines.Add("ALPINE");
        }
        return defines;
    }

    private bool IsActiveDefineRegionEnabled(Dictionary<string, bool> activeDefines, HashSet<string> enabledDefines)
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

    private static Dictionary<string, string> GenerateVariables(TestConfiguration config, DebuggeeConfiguration debuggeeConfig, Options options)
    {
        var vars = new Dictionary<string, string>();
        string debuggeeExe = debuggeeConfig.BinaryExePath;
        string dumpFileName = GenerateDumpFileName(config, Path.GetFileNameWithoutExtension(debuggeeExe), options);

        vars.Add("%DEBUGGEE_EXE%", debuggeeExe);
        if (dumpFileName != null)
        {
            vars.Add("%DUMP_NAME%", dumpFileName);
        }
        vars.Add("%DEBUG_ROOT%", debuggeeConfig.BinaryDirPath);
        vars.Add("%SOS_PATH%", config.SOSPath());

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
        foreach (KeyValuePair<string,string> kv in vars)
        {
            output = output.Replace(kv.Key, kv.Value);
        }
        return output;
    }

    class ScriptLogger : TestOutputProcessLogger
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

        readonly NativeDebugger _debugger;
        readonly List<Task<CommandResult>> _taskQueue;
        readonly StringBuilder _lastCommandOutput;
        TaskCompletionSource<CommandResult> _taskSource;

        public bool HasProcessExited { get; private set; }

        public ScriptLogger(NativeDebugger debugger, ITestOutputHelper output)
            : base(output)
        {
            lock (this)
            {
                _debugger = debugger;
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
                currentTask = _taskQueue[0];
                _taskQueue.RemoveAt(0);
            }
            return (await currentTask).CommandOutput != null;
        }

        public Task<CommandResult> WaitForCommandOutput()
        {
            Task<CommandResult> currentTask = null;
            lock (this)
            {
                currentTask = _taskQueue[0];
            }
            return currentTask;
        }

        public string ProcessCommand(string command)
        {
            switch (_debugger)
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

        public override void Write(ProcessRunner runner, string data, ProcessStream stream)
        {
            lock (this)
            {
                base.Write(runner, data, stream);
                if (stream == ProcessStream.StandardOut)
                {
                    _lastCommandOutput.Append(data);

                    string lastCommandOutput = _lastCommandOutput.ToString();
                    bool commandError = false;
                    bool commandEnd = false;

                    switch (_debugger)
                    {
                        case NativeDebugger.Cdb:
                        case NativeDebugger.Lldb:
                        case NativeDebugger.DotNetDump:
                            commandError = lastCommandOutput.EndsWith("<END_COMMAND_ERROR>");
                            commandEnd = commandError || lastCommandOutput.EndsWith("<END_COMMAND_OUTPUT>");
                            break;
                        case NativeDebugger.Gdb:
                            commandEnd = lastCommandOutput.EndsWith("(gdb) ");
                            break;
                        default:
                            throw new Exception("Debugger prompt not supported");
                    }

                    if (commandEnd)
                    {
                        FlushOutput();
                        _taskSource.TrySetResult(new CommandResult(lastCommandOutput, !commandError));
                        _lastCommandOutput.Clear();
                        AddTask();
                    }
                }
            }
        }

        public override void WriteLine(ProcessRunner runner, string data, ProcessStream stream)
        {
            lock (this)
            {
                base.WriteLine(runner, data, stream);
                if (stream == ProcessStream.StandardOut)
                {
                    _lastCommandOutput.AppendLine(data);
                }
            }
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
        if(string.IsNullOrEmpty(lldbPath))
        {
            lldbPath = Environment.GetEnvironmentVariable("LLDB_PATH");
        }
        return TestConfiguration.MakeCanonicalPath(lldbPath);
    }

    public static string GDBPath(this TestConfiguration config)
    {
        string gdbPath = config.GetValue("GDBPath");
        if(string.IsNullOrEmpty(gdbPath))
        {
            gdbPath = Environment.GetEnvironmentVariable("GDB_PATH");
        }
        return TestConfiguration.MakeCanonicalPath(gdbPath);
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

    public static string SOSHostRuntime(this TestConfiguration config)
    {
        return TestConfiguration.MakeCanonicalPath(config.GetValue("SOSHostRuntime"));
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
