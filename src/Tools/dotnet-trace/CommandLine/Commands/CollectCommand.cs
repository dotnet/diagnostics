// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Rendering;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools.Common;
using Microsoft.Internal.Common;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal class CollectCommandHandler
    {
        internal bool IsQuiet { get; set; }

        public CollectCommandHandler()
        {
            Console = new DefaultConsole(false);
            StartTraceSessionAsync = async (client, config, ct) => new CollectSession(await client.StartEventPipeSessionAsync(config, ct).ConfigureAwait(false));
            ResumeRuntimeAsync = (client, ct) => client.ResumeRuntimeAsync(ct);
            CollectSessionEventStream = (name) => new FileStream(name, FileMode.Create, FileAccess.Write);
        }

        private void ConsoleWriteLine(string str = "")
        {
            if (!IsQuiet)
            {
                Console.Out.WriteLine(str);
            }
        }

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
        /// <param name="format">If not using the default NetTrace format, an additional file will be emitted with the specified format under the same output name and with the corresponding format extension.</param>
        /// <param name="duration">The duration of trace to be taken. </param>
        /// <param name="clrevents">A list of CLR events to be emitted.</param>
        /// <param name="clreventlevel">The verbosity level of CLR events</param>
        /// <param name="diagnosticPort">Path to the diagnostic port to be used.</param>
        /// <param name="showchildio">Should IO from a child process be hidden.</param>
        /// <param name="resumeRuntime">Resume runtime once session has been initialized.</param>
        /// <param name="stoppingEventProviderName">A string, parsed as-is, that will stop the trace upon hitting an event with the matching provider name. For a more specific stopping event, additionally provide `--stopping-event-event-name` and/or `--stopping-event-payload-filter`.</param>
        /// <param name="stoppingEventEventName">A string, parsed as-is, that will stop the trace upon hitting an event with the matching event name. Requires `--stopping-event-provider-name` to be set. For a more specific stopping event, additionally provide `--stopping-event-payload-filter`.</param>
        /// <param name="stoppingEventPayloadFilter">A string, parsed as [payload_field_name]:[payload_field_value] pairs separated by commas, that will stop the trace upon hitting an event with a matching payload. Requires `--stopping-event-provider-name` and `--stopping-event-event-name` to be set.</param>
        /// <param name="rundown">Collect rundown events.</param>
        /// <returns></returns>
        internal async Task<int> Collect(CancellationToken ct, CommandLineConfiguration cliConfig, int processId, FileInfo output, uint buffersize, string[] providers, string[] profile, TraceFileFormat format, TimeSpan duration, string clrevents, string clreventlevel, string name, string diagnosticPort, bool showchildio, bool resumeRuntime, string stoppingEventProviderName, string stoppingEventEventName, string stoppingEventPayloadFilter, bool? rundown, string dsrouter)
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
                    if (CommandUtils.ResolveProcessForAttach(processId, name, diagnosticPort, dsrouter, out int resolvedProcessId))
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
                    ConsoleWriteLine("No profile or providers specified, defaulting to trace profiles 'dotnet-common' + 'dotnet-sampled-thread-time'.");
                    profile = new[] { "dotnet-common", "dotnet-sampled-thread-time" };
                }

                List<EventPipeProvider> providerCollection = ProviderUtils.ComputeProviderConfig(providers, clrevents, clreventlevel, profile, !IsQuiet, "collect", Console);
                if (providerCollection.Count <= 0)
                {
                    Console.Error.WriteLine("No providers were specified to start a trace.");
                    return (int)ReturnCode.ArgumentError;
                }

                long rundownKeyword = 0;
                RetryStrategy retryStrategy = RetryStrategy.NothingToRetry;
                foreach (string prof in profile)
                {
                    // Profiles are already validated in ComputeProviderConfig
                    Profile selectedProfile = ListProfilesCommandHandler.TraceProfiles
                        .FirstOrDefault(p => p.Name.Equals(prof, StringComparison.OrdinalIgnoreCase));

                    rundownKeyword |= selectedProfile.RundownKeyword;
                    if (selectedProfile.RetryStrategy > retryStrategy)
                    {
                        retryStrategy = selectedProfile.RetryStrategy;
                    }
                }
                if (rundownKeyword == 0)
                {
                    rundownKeyword = EventPipeSession.DefaultRundownKeyword;
                }
                if (rundown.HasValue)
                {
                    if (rundown.Value)
                    {
                        rundownKeyword |= EventPipeSession.DefaultRundownKeyword;
                        retryStrategy = (rundownKeyword == EventPipeSession.DefaultRundownKeyword) ? RetryStrategy.NothingToRetry : RetryStrategy.DropKeywordKeepRundown;
                    }
                    else
                    {
                        rundownKeyword = 0;
                        retryStrategy = RetryStrategy.NothingToRetry;
                    }
                }

                // Validate and parse stoppingEvent parameters: stoppingEventProviderName, stoppingEventEventName, stoppingEventPayloadFilter

                bool hasStoppingEventProviderName = !string.IsNullOrEmpty(stoppingEventProviderName);
                bool hasStoppingEventEventName = !string.IsNullOrEmpty(stoppingEventEventName);
                bool hasStoppingEventPayloadFilter = !string.IsNullOrEmpty(stoppingEventPayloadFilter);
                if (!hasStoppingEventProviderName && (hasStoppingEventEventName || hasStoppingEventPayloadFilter))
                {
                    Console.Error.WriteLine($"`{nameof(stoppingEventProviderName)}` is required to stop tracing after a specific event for a particular `{nameof(stoppingEventEventName)}` event name or `{nameof(stoppingEventPayloadFilter)}` payload filter.");
                    return (int)ReturnCode.ArgumentError;
                }
                if (!hasStoppingEventEventName && hasStoppingEventPayloadFilter)
                {
                    Console.Error.WriteLine($"`{nameof(stoppingEventEventName)}` is required to stop tracing after a specific event for a particular `{nameof(stoppingEventPayloadFilter)}` payload filter.");
                    return (int)ReturnCode.ArgumentError;
                }

                Dictionary<string, string> payloadFilter = new();
                if (hasStoppingEventPayloadFilter)
                {
                    string[] payloadFieldNameValuePairs = stoppingEventPayloadFilter.Split(',');
                    foreach (string pair in payloadFieldNameValuePairs)
                    {
                        string[] payloadFieldNameValuePair = pair.Split(':');
                        if (payloadFieldNameValuePair.Length != 2)
                        {
                            Console.Error.WriteLine($"`{nameof(stoppingEventPayloadFilter)}` does not have valid format. Ensure that it has `payload_field_name:payload_field_value` pairs separated by commas.");
                            return (int)ReturnCode.ArgumentError;
                        }

                        payloadFilter[payloadFieldNameValuePair[0]] = payloadFieldNameValuePair[1];
                    }
                }

                DiagnosticsClient diagnosticsClient;
                Process process = null;
                DiagnosticsClientBuilder builder = new("dotnet-trace", 10);
                ManualResetEvent shouldExit = new(false);
                ct.Register(() => shouldExit.Set());
                using (DiagnosticsClientHolder holder = await builder.Build(ct, processId, diagnosticPort, showChildIO: showchildio, printLaunchCommand: true).ConfigureAwait(false))
                {
                    string processMainModuleFileName = $"Process{processId}";

                    // if builder returned null, it means we received ctrl+C while waiting for clients to connect. Exit gracefully.
                    if (holder == null)
                    {
                        return (int)ReturnCode.Ok;
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

                    if (string.Equals(output.Name, CommonOptions.DefaultTraceName, StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime now = DateTime.Now;
                        FileInfo processMainModuleFileInfo = new(processMainModuleFileName);
                        output = new FileInfo($"{processMainModuleFileInfo.Name}_{now:yyyyMMdd}_{now:HHmmss}.nettrace");
                    }

                    bool shouldStopAfterDuration = duration != default(TimeSpan);
                    bool rundownRequested = false;
                    System.Timers.Timer durationTimer = null;

                    using (VirtualTerminalMode vTermMode = printStatusOverTime ? VirtualTerminalMode.TryEnable() : null)
                    {
                        ICollectSession session = null;
                        try
                        {
                            EventPipeSessionConfiguration config = new(providerCollection, (int)buffersize, rundownKeyword: rundownKeyword, requestStackwalk: true);
                            session = await StartTraceSessionAsync(diagnosticsClient, config, ct).ConfigureAwait(false);
                        }
                        catch (UnsupportedCommandException e)
                        {
                            if (retryStrategy == RetryStrategy.DropKeywordKeepRundown)
                            {
                                Console.Error.WriteLine("The runtime version being traced doesn't support the custom rundown feature used by this tracing configuration, retrying with the standard rundown keyword");
                                //
                                // If you are building new profiles or options, you can test with these asserts to make sure you are writing
                                // the retry strategies correctly.
                                //
                                // If these assert ever fires, it means something is wrong with the option generation logic leading to unnecessary retries.
                                // unnecessary retries is not fatal.
                                //
                                // Debug.Assert(rundownKeyword != 0);
                                // Debug.Assert(rundownKeyword != EventPipeSession.DefaultRundownKeyword);
                                //
                                EventPipeSessionConfiguration config = new(providerCollection, (int)buffersize, rundownKeyword: EventPipeSession.DefaultRundownKeyword, requestStackwalk: true);
                                session = await StartTraceSessionAsync(diagnosticsClient, config, ct).ConfigureAwait(false);
                            }
                            else if (retryStrategy == RetryStrategy.DropKeywordDropRundown)
                            {
                                Console.Error.WriteLine("The runtime version being traced doesn't support the custom rundown feature used by this tracing configuration, retrying with the rundown omitted");
                                //
                                // If you are building new profiles or options, you can test with these asserts to make sure you are writing
                                // the retry strategies correctly.
                                //
                                // If these assert ever fires, it means something is wrong with the option generation logic leading to unnecessary retries.
                                // unnecessary retries is not fatal.
                                //
                                // Debug.Assert(rundownKeyword != 0);
                                // Debug.Assert(rundownKeyword != EventPipeSession.DefaultRundownKeyword);
                                //
                                EventPipeSessionConfiguration config = new(providerCollection, (int)buffersize, rundownKeyword: 0, requestStackwalk: true);
                                session = await StartTraceSessionAsync(diagnosticsClient, config, ct).ConfigureAwait(false);
                            }
                            else
                            {
                                Console.Error.WriteLine($"Unable to start a tracing session: {e}");
                                return (int)ReturnCode.SessionCreationError;
                            }
                        }
                        catch (UnauthorizedAccessException e)
                        {
                            Console.Error.WriteLine($"dotnet-trace does not have permission to access the specified app: {e.GetType()}");
                            return (int)ReturnCode.SessionCreationError;
                        }
                        if (resumeRuntime)
                        {
                            try
                            {
                                await ResumeRuntimeAsync(diagnosticsClient, ct).ConfigureAwait(false);
                            }
                            catch (UnsupportedCommandException)
                            {
                                // Noop if command is unsupported, since the target is most likely a 3.1 app.
                            }
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

                        Stopwatch stopwatch = new();
                        durationTimer?.Start();
                        stopwatch.Start();

                        LineRewriter rewriter = null;

                        using (Stream eventStream = CollectSessionEventStream(output.FullName))
                        {
                            ConsoleWriteLine($"Process        : {processMainModuleFileName}");
                            ConsoleWriteLine($"Output File    : {output.FullName}");
                            if (shouldStopAfterDuration)
                            {
                                ConsoleWriteLine($"Trace Duration : {duration:dd\\:hh\\:mm\\:ss}");
                            }
                            ConsoleWriteLine();

                            EventMonitor eventMonitor = null;
                            Task copyTask = null;
                            if (hasStoppingEventProviderName)
                            {
                                eventMonitor = new(
                                    stoppingEventProviderName,
                                    stoppingEventEventName,
                                    payloadFilter,
                                    onEvent: (traceEvent) => {
                                        shouldExit.Set();
                                    },
                                    onPayloadFilterMismatch: (traceEvent) => {
                                        ConsoleWriteLine($"One or more field names specified in the payload filter for event '{traceEvent.ProviderName}/{traceEvent.EventName}' do not match any of the known field names: '{string.Join(' ', traceEvent.PayloadNames)}'. As a result the requested stopping event is unreachable; will continue to collect the trace for the remaining specified duration.");
                                    },
                                    eventStream: new PassthroughStream(session.EventStream, eventStream, (int)buffersize, leaveDestinationStreamOpen: true),
                                    callOnEventOnlyOnce: true);

                                copyTask = eventMonitor.ProcessAsync(CancellationToken.None);
                            }
                            else
                            {
                                copyTask = session.EventStream.CopyToAsync(eventStream);
                            }
                            Task shouldExitTask = copyTask.ContinueWith(
                                (task) => shouldExit.Set(),
                                CancellationToken.None,
                                TaskContinuationOptions.None,
                                TaskScheduler.Default);

                            if (printStatusOverTime)
                            {
                                rewriter = new LineRewriter(Console) { LineToClear = Console.CursorTop - 1 };
                                Console.CursorVisible = false;
                                if (!rewriter.IsRewriteConsoleLineSupported)
                                {
                                    ConsoleWriteLine("Recording trace in progress. Press <Enter> or <Ctrl+C> to exit...");
                                }
                            }

                            FileInfo fileInfo = new(output.FullName);
                            bool wroteStatus = false;
                            Action printStatus = () => {
                                if (printStatusOverTime && rewriter.IsRewriteConsoleLineSupported)
                                {
                                    if (wroteStatus)
                                    {
                                        rewriter?.RewriteConsoleLine();
                                    }
                                    else
                                    {
                                        // First time writing status, so don't rewrite console yet.
                                        wroteStatus = true;
                                    }
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

                                if (eventMonitor != null)
                                {
                                    await eventMonitor.DisposeAsync().ConfigureAwait(false);
                                }
                            }
                            // At this point the copyTask will have finished, so wait on the shouldExitTask in case it threw
                            // an exception or had some other interesting behavior
                            shouldExitTask.Wait();
                        }

                        ConsoleWriteLine();
                        ConsoleWriteLine("Trace completed.");

                        if (format != TraceFileFormat.NetTrace)
                        {
                            string outputFilename = TraceFileFormatConverter.GetConvertedFilename(output.FullName, outputfile: null, format);
                            TraceFileFormatConverter.ConvertToFormat(cliConfig.Output, cliConfig.Error, format, fileToConvert: output.FullName, outputFilename);
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
            catch (OperationCanceledException)
            {
                ConsoleWriteLine();
                ConsoleWriteLine("Trace collection canceled.");
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
                    if (!Console.IsOutputRedirected)
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
                DsRouterProcessLauncher.Launcher.Cleanup();
            }
            return ret;
        }

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

        public static Command CollectCommand()
        {
            Command collectCommand = new("collect")
            {
                // Options
                CommonOptions.ProcessIdOption,
                CircularBufferOption,
                CommonOptions.OutputPathOption,
                CommonOptions.ProvidersOption,
                CommonOptions.ProfileOption,
                CommonOptions.FormatOption,
                CommonOptions.DurationOption,
                CommonOptions.CLREventsOption,
                CommonOptions.CLREventLevelOption,
                CommonOptions.NameOption,
                DiagnosticPortOption,
                ShowChildIOOption,
                ResumeRuntimeOption,
                StoppingEventProviderNameOption,
                StoppingEventEventNameOption,
                StoppingEventPayloadFilterOption,
                RundownOption,
                DSRouterOption
            };
            collectCommand.TreatUnmatchedTokensAsErrors = false; // see the logic in Program.Main that handles UnmatchedTokens
            collectCommand.Description = "Collects a diagnostic trace from a currently running process or launch a child process and trace it. Append -- to the collect command to instruct the tool to run a command and trace it immediately. When tracing a child process, the exit code of dotnet-trace shall be that of the traced process unless the trace process encounters an error.";

            collectCommand.SetAction((parseResult, ct) =>
            {
                CollectCommandHandler handler = new();
                string providersValue = parseResult.GetValue(CommonOptions.ProvidersOption) ?? string.Empty;
                string profileValue = parseResult.GetValue(CommonOptions.ProfileOption) ?? string.Empty;

                return handler.Collect(ct,
                                       cliConfig: parseResult.Configuration,
                                       processId: parseResult.GetValue(CommonOptions.ProcessIdOption),
                                       output: parseResult.GetValue(CommonOptions.OutputPathOption),
                                       buffersize: parseResult.GetValue(CircularBufferOption),
                                       providers: providersValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                                       profile: profileValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                                       format: parseResult.GetValue(CommonOptions.FormatOption),
                                       duration: parseResult.GetValue(CommonOptions.DurationOption),
                                       clrevents: parseResult.GetValue(CommonOptions.CLREventsOption) ?? string.Empty,
                                       clreventlevel: parseResult.GetValue(CommonOptions.CLREventLevelOption) ?? string.Empty,
                                       name: parseResult.GetValue(CommonOptions.NameOption),
                                       diagnosticPort: parseResult.GetValue(DiagnosticPortOption) ?? string.Empty,
                                       showchildio: parseResult.GetValue(ShowChildIOOption),
                                       resumeRuntime: parseResult.GetValue(ResumeRuntimeOption),
                                       stoppingEventProviderName: parseResult.GetValue(StoppingEventProviderNameOption),
                                       stoppingEventEventName: parseResult.GetValue(StoppingEventEventNameOption),
                                       stoppingEventPayloadFilter: parseResult.GetValue(StoppingEventPayloadFilterOption),
                                       rundown: parseResult.GetValue(RundownOption),
                                       dsrouter: parseResult.GetValue(DSRouterOption));
            });

            return collectCommand;
        }

        private const uint DefaultCircularBufferSizeInMB = 256;

        private static readonly Option<uint> CircularBufferOption =
            new("--buffersize")
            {
                Description = $"Sets the size of the in-memory circular buffer in megabytes. Default {DefaultCircularBufferSizeInMB} MB.",
                DefaultValueFactory = _ => DefaultCircularBufferSizeInMB,
            };

        private static readonly Option<string> DiagnosticPortOption =
            new("--diagnostic-port", "--dport")
            {
                Description = @"The path to a diagnostic port to be used."
            };

        private static readonly Option<bool> ShowChildIOOption =
            new("--show-child-io")
            {
                Description = @"Shows the input and output streams of a launched child process in the current console."
            };

        private static readonly Option<bool> ResumeRuntimeOption =
            new("--resume-runtime")
            {
                Description = @"Resume runtime once session has been initialized, defaults to true. Disable resume of runtime using --resume-runtime:false",
                DefaultValueFactory = _ => true,
            };

        private static readonly Option<string> StoppingEventProviderNameOption =
            new("--stopping-event-provider-name")
            {
                Description = @"A string, parsed as-is, that will stop the trace upon hitting an event with the matching provider name. For a more specific stopping event, additionally provide `--stopping-event-event-name` and/or `--stopping-event-payload-filter`."
            };

        private static readonly Option<string> StoppingEventEventNameOption =
            new("--stopping-event-event-name")
            {
                Description = @"A string, parsed as-is, that will stop the trace upon hitting an event with the matching event name. Requires `--stopping-event-provider-name` to be set. For a more specific stopping event, additionally provide `--stopping-event-payload-filter`."
            };

        private static readonly Option<string> StoppingEventPayloadFilterOption =
            new("--stopping-event-payload-filter")
            {
                Description = @"A string, parsed as [payload_field_name]:[payload_field_value] pairs separated by commas, that will stop the trace upon hitting an event with a matching payload. Requires `--stopping-event-provider-name` and `--stopping-event-event-name` to be set."
            };

        private static readonly Option<bool?> RundownOption =
            new("--rundown")
            {
                Description = @"Collect rundown events unless specified false."
            };

        private static readonly Option<string> DSRouterOption =
            new("--dsrouter")
            {
                Description = @"The dsrouter command to start. Value should be one of ios, ios-sim, android, android-emu. Run `dotnet-dsrouter -h` for more information."
            };

#region testing seams
        // Abstraction for Collect's usage of EventPipeSession to facilitate testing.
        internal interface ICollectSession : IDisposable
        {
            Stream EventStream { get; }
            void Stop();
        }

        private sealed class CollectSession : ICollectSession
        {
            private readonly EventPipeSession _session;
            public CollectSession(EventPipeSession session) => _session = session;
            public Stream EventStream => _session.EventStream;
            public void Stop() => _session.Stop();
            public void Dispose() => _session.Dispose();
        }

        internal Func<DiagnosticsClient, EventPipeSessionConfiguration, CancellationToken, Task<ICollectSession>> StartTraceSessionAsync { get; set; }
        internal Func<DiagnosticsClient, CancellationToken, Task> ResumeRuntimeAsync { get; set; }
        internal Func<string, Stream> CollectSessionEventStream { get; set; }
        internal IConsole Console { get; set; }
#endregion
    }
}
