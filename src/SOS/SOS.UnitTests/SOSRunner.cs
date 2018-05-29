using Debugger.Tests.Build;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Debugger.Tests
{
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

        public enum NativeDebugger
        {
            Unknown,
            Cdb,
            Lldb,
            Gdb
        }

        public const string HexValueRegEx = "[A-Fa-f0-9]+(`[A-Fa-f0-9]+)?";
        public const string DecValueRegEx = "[0-9]+(`[0-9]+)?";

        public NativeDebugger Debugger { get; private set; }

        public string DebuggerToString
        {
            get { return Debugger.ToString().ToUpperInvariant(); }
        }

        private static int s_setExecuteOnDebuggers = 0;

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

        public static async Task<SOSRunner> StartDebugger(TestConfiguration config, ITestOutputHelper output, 
            string testName, string debuggeeName, string debuggeeArguments, bool loadDump = false, bool generateDump = false)
        {
            TestRunner.OutputHelper outputHelper = null;
            SOSRunner sosRunner = null;

            // Figure out which native debugger to use
            NativeDebugger debugger = GetNativeDebuggerToUse(generateDump);

            try
            {
                // Setup the logging from the options in the config file
                outputHelper = TestRunner.ConfigureLogging(config, output, testName);

                // Restore and build the debuggee. The debuggee name is lower cased because the 
                // source directory name has been lowercased by the build system.
                DebuggeeConfiguration debuggeeConfig = await DebuggeeCompiler.Execute(config, debuggeeName.ToLowerInvariant(), outputHelper);

                outputHelper.WriteLine("SOSRunner processing {0}", testName);
                outputHelper.WriteLine("{");

                var variables = GenerateVariables(config, debuggeeConfig, generateDump);
                var scriptLogger = new ScriptLogger(debugger, outputHelper.IndentedOutput);

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
                    throw new Exception("Native debugger path not set or does not exist: " + debuggerPath);
                }

                // Get the debugger arguments and commands to run initially
                List<string> initialCommands = new List<string>();
                string arguments = null;

                switch (debugger)
                {
                    case NativeDebugger.Cdb:
                        if (loadDump)
                        {
                            arguments = "-z %DUMP_NAME%";
                            initialCommands.Add(".sympath %DEBUG_ROOT%;srv*");
                        }
                        else
                        {
                            arguments = "-Gsins " + debuggeeCommandLine;
                            initialCommands.Add(".sympath %DEBUG_ROOT%;srv*");
                            // disable stopping on integer divide-by-zero and integer overflow exceptions
                            initialCommands.Add("sxd dz");  
                            initialCommands.Add("sxd iov");  
                        }
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
                        string lldbHelperScript = config.LLDBHelperScript;
                        if (string.IsNullOrWhiteSpace(lldbHelperScript) || !File.Exists(lldbHelperScript))
                        {
                            throw new Exception("LLDB helper script path not set or does not exist: " + lldbHelperScript);
                        }
                        arguments = string.Format(@"--no-lldbinit -o ""settings set interpreter.prompt-on-quit false"" -o ""command script import {0}""", lldbHelperScript);

                        // Load the dump or launch the debuggee process
                        if (loadDump)
                        {
                            initialCommands.Add(string.Format(@"target create --core ""%DUMP_NAME%"" ""{0}""", config.HostExe));
                        }
                        else
                        {
                            initialCommands.Add(string.Format(@"target create ""{0}""", config.HostExe));
                            if (!string.IsNullOrWhiteSpace(config.HostArgs))
                            {
                                initialCommands.Add(string.Format(@"settings append target.run-args ""{0}""", ReplaceVariables(variables, config.HostArgs)));
                            }
                            initialCommands.Add(string.Format(@"settings append target.run-args ""{0}""", debuggeeConfig.BinaryExePath));
                            if (!string.IsNullOrWhiteSpace(debuggeeArguments))
                            {
                                initialCommands.Add(string.Format(@"settings append target.run-args ""{0}""", ReplaceVariables(variables, debuggeeArguments)));
                            }
                            initialCommands.Add("process launch -s");
                            initialCommands.Add("process handle -s false -n false -p true SIGFPE");
                            initialCommands.Add("process handle -s false -n false -p true SIGSEGV");
                            initialCommands.Add("process handle -s true -n true -p true SIGABRT");
                        }
                        break;
                    case NativeDebugger.Gdb:
                        if (loadDump)
                        {
                            throw new Exception("GDB not meant for loading core dumps");
                        }
                        arguments = "--args " + debuggeeCommandLine;
                        initialCommands.Add("handle SIGFPE nostop noprint");
                        initialCommands.Add("handle SIGSEGV nostop noprint");
                        initialCommands.Add("handle SIGABRT stop print");
                        initialCommands.Add("start");
                        break;
                }

                if (OS.Kind != OSKind.Windows)
                {
                    if (Interlocked.Exchange(ref s_setExecuteOnDebuggers, 1) == 0)
                    {
                        // The binaries from the lldb and gdb packages don't have the execute bit set 
                        // so set it now first time the SOS tests are run.
                        var sl = new ScriptLogger(debugger, outputHelper.IndentedOutput);

                        // Will also set execute on gdb when the gdb package is ready.
                        ProcessRunner pr = new ProcessRunner(
                            "/bin/bash", 
                            string.Format(@"-c ""/bin/chmod +x {0}/* {0}/../lib/*""",
                            Path.GetDirectoryName(config.LLDBPath))).
                                WithLog(sl);

                        pr.Start();

                        await pr.WaitForExit();
                    }
                }

                // Create the native debugger process running
                ProcessRunner processRunner = new ProcessRunner(debuggerPath, ReplaceVariables(variables, arguments)).
                    WithLog(scriptLogger).
                    WithTimeout(TimeSpan.FromMinutes(5));

                // Create the sos runner instance
                sosRunner = new SOSRunner(debugger, config, outputHelper, variables, scriptLogger, processRunner, loadDump);

                // Start the native debugger
                processRunner.Start();

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
                throw new Exception("Script file does not exist: " + scriptFile);
            }
            List<string> enabledDefines = GetEnabledDefines();
            LogProcessingReproInfo(scriptFile, enabledDefines);
            string[] scriptLines = File.ReadAllLines(scriptFile);
            List<string> activeDefines = new List<string>();
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
                        activeDefines.Add(define);
                        isActiveDefineRegionEnabled = IsActiveDefineRegionEnabled(activeDefines, enabledDefines);
                    }
                    else if (line.StartsWith("ENDIF:"))
                    {
                        string define = line.Substring("ENDIF:".Length);
                        if (!activeDefines.Last().Equals(define))
                        {
                            throw new Exception("Mismatched IFDEF/ENDIF. IFDEF: " + activeDefines.Last() + " ENDIF: " + define);
                        }
                        activeDefines.Remove(define);
                        isActiveDefineRegionEnabled = IsActiveDefineRegionEnabled(activeDefines, enabledDefines);
                    }
                    else if (!isActiveDefineRegionEnabled)
                    {
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
                        await RunSosCommand(input);
                    }
                    else if (line.StartsWith("COMMAND:"))
                    {
                        string input = line.Substring("COMMAND:".Length).TrimStart();
                        await RunCommand(input);
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
                    throw new Exception("Error unbalanced IFDEFs. " + activeDefines[0] + " has no ENDIF.");
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
            List<string> commands = new List<string>();
            switch (Debugger)
            {
                case NativeDebugger.Cdb:
                    commands.Add(".load " + _config.SOSPath);
                    commands.Add(".lines; .reload");
                    break;
                case NativeDebugger.Lldb:
                    commands.Add("plugin load " + _config.SOSPath);
                    if (_isDump)
                    {
                        // lldb doesn't load dump with the initial thread set to one with
                        // the exception. This SOS command looks for a thread with a managed
                        // exception and set the current thread to it.
                        commands.Add("clrthreads -managedexception");
                    }
                    break;
                default:
                    throw new Exception(DebuggerToString + " cannot load sos extension");
            }
            await RunCommands(commands);
        }

        public async Task ContinueExecution()
        {
            string command = null;
            switch (Debugger)
            {
                case NativeDebugger.Cdb:
                    command = "g";
                    break;
                case NativeDebugger.Lldb:
                    command = "process continue";
                    break;
                case NativeDebugger.Gdb:
                    command = "continue";
                    break;
            }
            await RunCommand(command);
        }

        public async Task<string> RunSosCommand(string command)
        {
            switch (Debugger)
            {
                case NativeDebugger.Cdb:
                    command = "!" + command;
                    break;
                case NativeDebugger.Lldb:
                    command = "sos " + command;
                    break;
                default:
                    throw new Exception(DebuggerToString + " cannot execute sos command");
            }
            return await RunCommand(command);
        }

        public async Task RunCommands(IEnumerable<string> commands)
        {
            foreach (string command in commands)
            {
                await RunCommand(command);
            }
        }

        public async Task<string> RunCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new Exception("Debugger command empty or null");
            }
            return await HandleCommand(command);
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

        public static string GenerateDumpFileName(TestConfiguration config, string debuggeeName, bool generateDump)
        {
            string dumpRoot = generateDump ? config.DebuggeeDumpOutputRootDir : config.DebuggeeDumpInputRootDir;
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

        private static NativeDebugger GetNativeDebuggerToUse(bool generateDump)
        {
            switch (OS.Kind)
            {
                case OSKind.Windows:
                    return NativeDebugger.Cdb;

                case OSKind.Linux:
                case OSKind.FreeBSD:
                    return NativeDebugger.Lldb;

                case OSKind.OSX:
                   if (generateDump)
                    {
                        return NativeDebugger.Gdb;
                    }
                    else
                    {
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
                    return config.CDBPath;

                case NativeDebugger.Lldb:
                    return config.LLDBPath;

                case NativeDebugger.Gdb:
                    return config.GDBPath;
            }

            return null;
        }

        private async Task<string> HandleCommand(string input)
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

            // The POUT convention is to write a commnd like this:
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
            
            _processRunner.StandardInputWriteLine(_scriptLogger.ProcessCommand(ReplaceVariables(input)));
            _lastCommandOutput = await _scriptLogger.WaitForCommandOutput();
            return _lastCommandOutput;
        }

        private void LogProcessingReproInfo(string scriptFile, List<string> enabledDefines)
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

        private List<string> GetEnabledDefines()
        {
            List<string> defines = new List<string>();
            defines.Add(OS.Kind.ToString().ToUpperInvariant());
            defines.Add(DebuggerToString);
            defines.Add(_config.TestProduct.ToUpperInvariant());
            if (_isDump)
            {
                defines.Add("DUMP");
            }
            else
            {
                defines.Add("LIVE");
            }
            if (_config.TargetArchitecture.Equals("x86"))
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
            return defines;
        }

        private bool IsActiveDefineRegionEnabled(List<string> activeDefines, List<string> enabledDefines)
        {
            foreach (string activeDefine in activeDefines)
            {
                if (!enabledDefines.Contains(activeDefine))
                {
                    return false;
                }
            }
            return true;
        }

        private static Dictionary<string, string> GenerateVariables(TestConfiguration config, DebuggeeConfiguration debuggeeConfig, bool generateDump)
        {
            Dictionary<string, string> vars = new Dictionary<string, string>();
            string debuggeeExe = debuggeeConfig.BinaryExePath;
            string dumpFileName = GenerateDumpFileName(config, Path.GetFileNameWithoutExtension(debuggeeExe), generateDump);

            vars.Add("%DEBUGGEE_EXE%", debuggeeExe);
            if (dumpFileName != null)
            {
                vars.Add("%DUMP_NAME%", dumpFileName);
            }
            vars.Add("%DEBUG_ROOT%", debuggeeConfig.BinaryDirPath);
            vars.Add("%SOS_PATH%", config.SOSPath);

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
            readonly NativeDebugger _debugger;
            readonly List<Task<string>> _taskQueue;
            readonly StringBuilder _lastCommandOutput;
            TaskCompletionSource<string> _taskSource;

            public bool HasProcessExited { get; private set; }

            public ScriptLogger(NativeDebugger debugger, ITestOutputHelper output)
                : base(output)
            {
                lock (this)
                {
                    _debugger = debugger;
                    _lastCommandOutput = new StringBuilder();
                    _taskQueue = new List<Task<string>>();
                    AddTask();
                }
            }

            private void AddTask()
            {
                _taskSource = new TaskCompletionSource<string>();
                _taskQueue.Add(_taskSource.Task);
            }

            public async Task<bool> WaitForCommandPrompt()
            {
                Task<string> currentTask = null;
                lock (this)
                {
                    currentTask = _taskQueue[0];
                    _taskQueue.RemoveAt(0);
                }
                return (await currentTask) != null;
            }

            public Task<string> WaitForCommandOutput()
            {
                Task<string> currentTask = null;
                lock (this)
                {
                    currentTask = _taskQueue[0];
                }
                return currentTask;
            }

            public string ProcessCommand(string command)
            {
                if (_debugger == NativeDebugger.Lldb)
                {
                    command = string.Format("runcommand {0}", command);
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

                        string prompt;
                        switch (_debugger)
                        {
                            case NativeDebugger.Cdb:
                                // Some commands like DumpStack have ===> or -> in the output that looks 
                                // like the cdb prompt. Using a regex here to better match the cdb prompt
                                // is way to slow. 
                                if (lastCommandOutput.EndsWith("=> ") || lastCommandOutput.EndsWith("-> "))
                                {
                                    return;
                                }
                                prompt = "> ";
                                break;
                            case NativeDebugger.Lldb:
                                prompt = "<END_COMMAND_OUTPUT>";
                                break;
                            case NativeDebugger.Gdb:
                                prompt = "(gdb) ";
                                break;
                            default:
                                throw new Exception("Debugger prompt not supported");
                        }

                        if (lastCommandOutput.EndsWith(prompt))
                        {
                            FlushOutput();
                            _taskSource.TrySetResult(lastCommandOutput);
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
                    _taskSource.TrySetResult(null);
                }
            }
        }
    }
}
