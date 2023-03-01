// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Rendering;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CollectCommandHandler
    {
        internal static bool IsQuiet
        { get; set; }

        private static void ConsoleWriteLine(string str)
        {
            if (!IsQuiet)
            {
                Console.Out.WriteLine(str);
            }
        }

        private delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration, string clrevents, string clreventlevel, string name, string port, bool showchildio, bool resumeRuntime);

        /// <summary>
        /// Collects a diagnostic trace from a currently running process or launch a child process and trace it.
        /// Append -- to the collect command to instruct the tool to run a command and trace it immediately. By default the IO from this process is hidden, but the --show-child-io option may be used to show the child process IO.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the trace from.</param>
        /// <param name="name">The name of process to collect the trace from.</param>
        /// <param name="output">The output path for the collected trace data.</param>
        /// <param name="buffersize">Sets the size of the in-memory circular buffer in megabytes.</param>
        /// <param name="providers">A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'</param>
        /// <param name="profile">A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.</param>
        /// <param name="format">The desired format of the created trace file.</param>
        /// <param name="duration">The duration of trace to be taken. </param>
        /// <param name="clrevents">A list of CLR events to be emitted.</param>
        /// <param name="clreventlevel">The verbosity level of CLR events</param>
        /// <param name="diagnosticPort">Path to the diagnostic port to be used.</param>
        /// <param name="showchildio">Should IO from a child process be hidden.</param>
        /// <param name="resumeRuntime">Resume runtime once session has been initialized.</param>
        /// <returns></returns>
        private static async Task<int> Collect(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration, string clrevents, string clreventlevel, string name, string diagnosticPort, bool showchildio, bool resumeRuntime)
        {
            bool collectionStopped = false;
            bool cancelOnEnter = true;
            bool cancelOnCtrlC = true;
            bool printStatusOverTime = true;
            int ret = (int)ReturnCode.Ok;
            IsQuiet = showchildio;

            try
            {
                Debug.Assert(output != null);
                Debug.Assert(profile != null);

                if (ProcessLauncher.Launcher.HasChildProc && showchildio)
                {
                    // If showing IO, then all IO (including CtrlC) behavior is delegated to the child process
                    cancelOnCtrlC = false;
                    cancelOnEnter = false;
                    printStatusOverTime = false;
                }
                else
                {
                    cancelOnCtrlC = true;
                    cancelOnEnter = !Console.IsInputRedirected;
                    printStatusOverTime = !Console.IsOutputRedirected;
                }

                if (!cancelOnCtrlC)
                {
                    ct = CancellationToken.None;
                }

                if (!ProcessLauncher.Launcher.HasChildProc)
                {
                    if (showchildio)
                    {
                        Console.WriteLine("--show-child-io must not be specified when attaching to a process");
                        return (int)ReturnCode.ArgumentError;
                    }
                    if (CommandUtils.ValidateArgumentsForAttach(processId, name, diagnosticPort, out int resolvedProcessId))
                    {
                        processId = resolvedProcessId;
                    }
                    else
                    {
                        return (int)ReturnCode.ArgumentError;
                    }
                }
                else if (!CommandUtils.ValidateArgumentsForChildProcess(processId, name, diagnosticPort))
                {
                    return (int)ReturnCode.ArgumentError;
                }

                if (profile.Length == 0 && providers.Length == 0 && clrevents.Length == 0)
                {
                    ConsoleWriteLine("No profile or providers specified, defaulting to trace profile 'cpu-sampling'");
                    profile = "cpu-sampling";
                }

                Dictionary<string, string> enabledBy = new Dictionary<string, string>();

                List<EventPipeProvider> providerCollection = Extensions.ToProviders(providers);
                foreach (EventPipeProvider providerCollectionProvider in providerCollection)
                {
                    enabledBy[providerCollectionProvider.Name] = "--providers ";
                }

                if (profile.Length != 0)
                {
                    Profile selectedProfile = ListProfilesCommandHandler.DotNETRuntimeProfiles
                        .FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));
                    if (selectedProfile == null)
                    {
                        Console.Error.WriteLine($"Invalid profile name: {profile}");
                        return (int)ReturnCode.ArgumentError;
                    }

                    Profile.MergeProfileAndProviders(selectedProfile, providerCollection, enabledBy);
                }

                // Parse --clrevents parameter
                if (clrevents.Length != 0)
                {
                    // Ignore --clrevents if CLR event provider was already specified via --profile or --providers command.
                    if (enabledBy.ContainsKey(Extensions.CLREventProviderName))
                    {
                        ConsoleWriteLine($"The argument --clrevents {clrevents} will be ignored because the CLR provider was configured via either --profile or --providers command.");
                    }
                    else
                    {
                        EventPipeProvider clrProvider = Extensions.ToCLREventPipeProvider(clrevents, clreventlevel);
                        providerCollection.Add(clrProvider);
                        enabledBy[Extensions.CLREventProviderName] = "--clrevents";
                    }
                }


                if (providerCollection.Count <= 0)
                {
                    Console.Error.WriteLine("No providers were specified to start a trace.");
                    return (int)ReturnCode.ArgumentError;
                }

                PrintProviders(providerCollection, enabledBy);

                DiagnosticsClient diagnosticsClient;
                Process process = null;
                DiagnosticsClientBuilder builder = new DiagnosticsClientBuilder("dotnet-trace", 10);
                var shouldExit = new ManualResetEvent(false);
                ct.Register(() => shouldExit.Set());
                using (DiagnosticsClientHolder holder = await builder.Build(ct, processId, diagnosticPort, showChildIO: showchildio, printLaunchCommand: true).ConfigureAwait(false))
                {
                    string processMainModuleFileName = $"Process{processId}";

                    // if builder returned null, it means we received ctrl+C while waiting for clients to connect. Exit gracefully.
                    if (holder == null)
                    {
                        return (int)await Task.FromResult(ReturnCode.Ok).ConfigureAwait(false);
                    }
                    diagnosticsClient = holder.Client;
                    if (ProcessLauncher.Launcher.HasChildProc)
                    {
                        process = Process.GetProcessById(holder.EndpointInfo.ProcessId);
                    }
                    else if (IpcEndpointConfig.TryParse(diagnosticPort, out IpcEndpointConfig portConfig) && (portConfig.IsConnectConfig || portConfig.IsListenConfig))
                    {
                        // No information regarding process (could even be a routed process),
                        // use "file" part of IPC channel name as process main module file name.
                        processMainModuleFileName = Path.GetFileName(portConfig.Address);
                    }
                    else
                    {
                        process = Process.GetProcessById(processId);
                    }

                    if (process != null)
                    {
                        // Reading the process MainModule filename can fail if the target process closes
                        // or isn't fully setup. Retry a few times to attempt to address the issue
                        for (int attempts = 0; attempts < 10; attempts++)
                        {
                            try
                            {
                                processMainModuleFileName = process.MainModule.FileName;
                                break;
                            }

                            catch
                            {
                                Thread.Sleep(200);
                            }
                        }

                    }

                    if (string.Equals(output.Name, DefaultTraceName, StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime now = DateTime.Now;
                        var processMainModuleFileInfo = new FileInfo(processMainModuleFileName);
                        output = new FileInfo($"{processMainModuleFileInfo.Name}_{now:yyyyMMdd}_{now:HHmmss}.nettrace");
                    }

                    bool shouldStopAfterDuration = duration != default(TimeSpan);
                    bool rundownRequested = false;
                    System.Timers.Timer durationTimer = null;


                    using (VirtualTerminalMode vTermMode = printStatusOverTime ? VirtualTerminalMode.TryEnable() : null)
                    {
                        EventPipeSession session = null;
                        try
                        {
                            session = diagnosticsClient.StartEventPipeSession(providerCollection, true, (int)buffersize);
                            if (resumeRuntime)
                            {
                                try
                                {
                                    diagnosticsClient.ResumeRuntime();
                                }
                                catch (UnsupportedCommandException)
                                {
                                    // Noop if command is unsupported, since the target is most likely a 3.1 app.
                                }
                            }
                        }
                        catch (DiagnosticsClientException e)
                        {
                            Console.Error.WriteLine($"Unable to start a tracing session: {e}");
                            return (int)ReturnCode.SessionCreationError;
                        }
                        catch (UnauthorizedAccessException e)
                        {
                            Console.Error.WriteLine($"dotnet-trace does not have permission to access the specified app: {e.GetType()}");
                            return (int)ReturnCode.SessionCreationError;
                        }

                        if (session == null)
                        {
                            Console.Error.WriteLine("Unable to create session.");
                            return (int)ReturnCode.SessionCreationError;
                        }

                        if (shouldStopAfterDuration)
                        {
                            durationTimer = new System.Timers.Timer(duration.TotalMilliseconds);
                            durationTimer.Elapsed += (s, e) => shouldExit.Set();
                            durationTimer.AutoReset = false;
                        }

                        var stopwatch = new Stopwatch();
                        durationTimer?.Start();
                        stopwatch.Start();

                        LineRewriter rewriter = null;

                        using (var fs = new FileStream(output.FullName, FileMode.Create, FileAccess.Write))
                        {
                            ConsoleWriteLine($"Process        : {processMainModuleFileName}");
                            ConsoleWriteLine($"Output File    : {fs.Name}");
                            if (shouldStopAfterDuration)
                            {
                                ConsoleWriteLine($"Trace Duration : {duration:dd\\:hh\\:mm\\:ss}");
                            }

                            ConsoleWriteLine("\n\n");

                            var fileInfo = new FileInfo(output.FullName);
                            Task copyTask = session.EventStream.CopyToAsync(fs);
                            Task shouldExitTask = copyTask.ContinueWith(
                                (task) => shouldExit.Set(),
                                CancellationToken.None,
                                TaskContinuationOptions.None,
                                TaskScheduler.Default);

                            if (printStatusOverTime)
                            {
                                rewriter = new LineRewriter { LineToClear = Console.CursorTop - 1 };
                                Console.CursorVisible = false;
                            }

                            Action printStatus = () => {
                                if (printStatusOverTime)
                                {
                                    rewriter?.RewriteConsoleLine();
                                    fileInfo.Refresh();
                                    ConsoleWriteLine($"[{stopwatch.Elapsed:dd\\:hh\\:mm\\:ss}]\tRecording trace {GetSize(fileInfo.Length)}");
                                    ConsoleWriteLine("Press <Enter> or <Ctrl+C> to exit...");
                                }

                                if (rundownRequested)
                                {
                                    ConsoleWriteLine("Stopping the trace. This may take several minutes depending on the application being traced.");
                                }
                            };

                            while (!shouldExit.WaitOne(100) && !(cancelOnEnter && Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter))
                            {
                                printStatus();
                            }

                            // if the CopyToAsync ended early (target program exited, etc.), then we don't need to stop the session.
                            if (!copyTask.Wait(0))
                            {
                                // Behavior concerning Enter moving text in the terminal buffer when at the bottom of the buffer
                                // is different between Console/Terminals on Windows and Mac/Linux
                                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                                    printStatusOverTime &&
                                    rewriter != null &&
                                    Math.Abs(Console.CursorTop - Console.BufferHeight) == 1)
                                {
                                    rewriter.LineToClear--;
                                }
                                collectionStopped = true;
                                durationTimer?.Stop();
                                rundownRequested = true;
                                session.Stop();

                                do
                                {
                                    printStatus();
                                } while (!copyTask.Wait(100));
                            }
                            // At this point the copyTask will have finished, so wait on the shouldExitTask in case it threw
                            // an exception or had some other interesting behavior
                            shouldExitTask.Wait();
                        }

                        ConsoleWriteLine($"\nTrace completed.");

                        if (format != TraceFileFormat.NetTrace)
                        {
                            TraceFileFormatConverter.ConvertToFormat(format, output.FullName);
                        }
                    }

                    if (!collectionStopped && !ct.IsCancellationRequested)
                    {
                        // If the process is shutting down by itself print the return code from the process.
                        // Capture this before leaving the using, as the Dispose of the DiagnosticsClientHolder
                        // may terminate the target process causing it to have the wrong error code
                        if (ProcessLauncher.Launcher.HasChildProc && ProcessLauncher.Launcher.ChildProc.WaitForExit(5000))
                        {
                            ret = ProcessLauncher.Launcher.ChildProc.ExitCode;
                            ConsoleWriteLine($"Process exited with code '{ret}'.");
                            collectionStopped = true;
                        }
                    }
                }
            }
            catch (CommandLineErrorException e)
            {
                Console.Error.WriteLine($"[ERROR] {e.Message}");
                collectionStopped = true;
                ret = (int)ReturnCode.TracingError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex}");
                collectionStopped = true;
                ret = (int)ReturnCode.TracingError;
            }
            finally
            {
                if (printStatusOverTime)
                {
                    if (console.GetTerminal() != null)
                    {
                        Console.CursorVisible = true;
                    }
                }

                if (ProcessLauncher.Launcher.HasChildProc)
                {
                    if (!collectionStopped || ct.IsCancellationRequested)
                    {
                        ret = (int)ReturnCode.TracingError;
                    }
                }
                ProcessLauncher.Launcher.Cleanup();
            }
            return await Task.FromResult(ret).ConfigureAwait(false);
        }

        private static void PrintProviders(IReadOnlyList<EventPipeProvider> providers, Dictionary<string, string> enabledBy)
        {
            ConsoleWriteLine("");
            ConsoleWriteLine(string.Format("{0, -40}", "Provider Name") + string.Format("{0, -20}", "Keywords") +
                string.Format("{0, -20}", "Level") + "Enabled By");  // +4 is for the tab
            foreach (EventPipeProvider provider in providers)
            {
                ConsoleWriteLine(string.Format("{0, -80}", $"{GetProviderDisplayString(provider)}") + $"{enabledBy[provider.Name]}");
            }
            ConsoleWriteLine("");
        }
        private static string GetProviderDisplayString(EventPipeProvider provider) =>
            string.Format("{0, -40}", provider.Name) + string.Format("0x{0, -18}", $"{provider.Keywords:X16}") + string.Format("{0, -8}", provider.EventLevel.ToString() + $"({(int)provider.EventLevel})");

        private static string GetSize(long length)
        {
            if (length > 1e9)
            {
                return string.Format("{0,-8} (GB)", $"{length / 1e9:0.00##}");
            }
            else if (length > 1e6)
            {
                return string.Format("{0,-8} (MB)", $"{length / 1e6:0.00##}");
            }
            else if (length > 1e3)
            {
                return string.Format("{0,-8} (KB)", $"{length / 1e3:0.00##}");
            }
            else
            {
                return string.Format("{0,-8} (B)", $"{length / 1.0:0.00##}");
            }
        }

        public static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process or launch a child process and trace it. Append -- to the collect command to instruct the tool to run a command and trace it immediately. When tracing a child process, the exit code of dotnet-trace shall be that of the traced process unless the trace process encounters an error.")
            {
                // Handler
                HandlerDescriptor.FromDelegate((CollectDelegate)Collect).GetCommandHandler(),
                // Options
                CommonOptions.ProcessIdOption(),
                CircularBufferOption(),
                OutputPathOption(),
                ProvidersOption(),
                ProfileOption(),
                CommonOptions.FormatOption(),
                DurationOption(),
                CLREventsOption(),
                CLREventLevelOption(),
                CommonOptions.NameOption(),
                DiagnosticPortOption(),
                ShowChildIOOption(),
                ResumeRuntimeOption()
            };

        private static uint DefaultCircularBufferSizeInMB() => 256;

        private static Option CircularBufferOption() =>
            new Option(
                alias: "--buffersize",
                description: $"Sets the size of the in-memory circular buffer in megabytes. Default {DefaultCircularBufferSizeInMB()} MB.")
            {
                Argument = new Argument<uint>(name: "size", getDefaultValue: DefaultCircularBufferSizeInMB)
            };

        public static string DefaultTraceName => "default";

        private static Option OutputPathOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: $"The output path for the collected trace data. If not specified it defaults to '<appname>_<yyyyMMdd>_<HHmmss>.nettrace', e.g., 'myapp_20210315_111514.nettrace'.")
            {
                Argument = new Argument<FileInfo>(name: "trace-file-path", getDefaultValue: () => new FileInfo(DefaultTraceName))
            };

        private static Option ProvidersOption() =>
            new Option(
                alias: "--providers",
                description: @"A comma delimitted list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]'," +
                             @"where Provider is in the form: 'KnownProviderName[:[Flags][:[Level][:[KeyValueArgs]]]]', and KeyValueArgs is in the form: " +
                             @"'[key1=value1][;key2=value2]'.  Values in KeyValueArgs that contain ';' or '=' characters need to be surrounded by '""', " +
                             @"e.g., FilterAndPayloadSpecs=""MyProvider/MyEvent:-Prop1=Prop1;Prop2=Prop2.A.B;"".  Depending on your shell, you may need to " +
                             @"escape the '""' characters and/or surround the entire provider specification in quotes, e.g., " +
                             @"--providers 'KnownProviderName:0x1:1:FilterSpec=\""KnownProviderName/EventName:-Prop1=Prop1;Prop2=Prop2.A.B;\""'. These providers are in " +
                             @"addition to any providers implied by the --profile argument. If there is any discrepancy for a particular provider, the " +
                             @"configuration here takes precedence over the implicit configuration from the profile.  See documentation for examples.")
            {
                Argument = new Argument<string>(name: "list-of-comma-separated-providers", getDefaultValue: () => string.Empty) // TODO: Can we specify an actual type?
            };

        private static Option ProfileOption() =>
            new Option(
                alias: "--profile",
                description: @"A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.")
            {
                Argument = new Argument<string>(name: "profile-name", getDefaultValue: () => string.Empty)
            };

        private static Option DurationOption() =>
            new Option(
                alias: "--duration",
                description: @"When specified, will trace for the given timespan and then automatically stop the trace. Provided in the form of dd:hh:mm:ss.")
            {
                Argument = new Argument<TimeSpan>(name: "duration-timespan", getDefaultValue: () => default)
            };

        private static Option CLREventsOption() =>
            new Option(
                alias: "--clrevents",
                description: @"List of CLR runtime events to emit.")
            {
                Argument = new Argument<string>(name: "clrevents", getDefaultValue: () => string.Empty)
            };

        private static Option CLREventLevelOption() =>
            new Option(
                alias: "--clreventlevel",
                description: @"Verbosity of CLR events to be emitted.")
            {
                Argument = new Argument<string>(name: "clreventlevel", getDefaultValue: () => string.Empty)
            };
        private static Option DiagnosticPortOption() =>
            new Option(
                alias: "--diagnostic-port",
                description: @"The path to a diagnostic port to be used.")
            {
                Argument = new Argument<string>(name: "diagnosticPort", getDefaultValue: () => string.Empty)
            };
        private static Option ShowChildIOOption() =>
            new Option(
                alias: "--show-child-io",
                description: @"Shows the input and output streams of a launched child process in the current console.")
            {
                Argument = new Argument<bool>(name: "show-child-io", getDefaultValue: () => false)
            };

        private static Option ResumeRuntimeOption() =>
            new Option(
                alias: "--resume-runtime",
                description: @"Resume runtime once session has been initialized, defaults to true. Disable resume of runtime using --resume-runtime:false")
            {
                Argument = new Argument<bool>(name: "resumeRuntime", getDefaultValue: () => true)
            };
    }
}
