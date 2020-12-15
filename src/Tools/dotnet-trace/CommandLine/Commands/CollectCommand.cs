﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;
using Microsoft.Tools.Common;
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

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CollectCommandHandler
    {
        delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration, string clrevents, string clreventlevel, string name, string port);

        /// <summary>
        /// Collects a diagnostic trace from a currently running process.
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
        /// <param name="port">Path to the diagnostic port to be created.</param>
        /// <returns></returns>
        private static async Task<int> Collect(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration, string clrevents, string clreventlevel, string name, string diagnosticPort)
        {
            int ret = 0;
            try
            {
                Debug.Assert(output != null);
                Debug.Assert(profile != null);

                if (!ProcessLauncher.Launcher.HasChildProc)
                {
                    if (CommandUtils.ValidateArguments(processId, name, diagnosticPort, out int resolvedProcessId))
                    {
                        processId = resolvedProcessId;
                    }
                    else
                    {
                        return ErrorCodes.ArgumentError;
                    }
                }

                if (profile.Length == 0 && providers.Length == 0 && clrevents.Length == 0)
                {
                    Console.Out.WriteLine("No profile or providers specified, defaulting to trace profile 'cpu-sampling'");
                    profile = "cpu-sampling";
                }

                Dictionary<string, string> enabledBy = new Dictionary<string, string>();

                var providerCollection = Extensions.ToProviders(providers);
                foreach (EventPipeProvider providerCollectionProvider in providerCollection)
                {
                    enabledBy[providerCollectionProvider.Name] = "--providers ";
                }

                if (profile.Length != 0)
                {
                    var selectedProfile = ListProfilesCommandHandler.DotNETRuntimeProfiles
                        .FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));
                    if (selectedProfile == null)
                    {
                        Console.Error.WriteLine($"Invalid profile name: {profile}");
                        return ErrorCodes.ArgumentError;
                    }

                    Profile.MergeProfileAndProviders(selectedProfile, providerCollection, enabledBy);
                }

                // Parse --clrevents parameter
                if (clrevents.Length != 0)
                {
                    // Ignore --clrevents if CLR event provider was already specified via --profile or --providers command.
                    if (enabledBy.ContainsKey(Extensions.CLREventProviderName))
                    {
                        Console.WriteLine($"The argument --clrevents {clrevents} will be ignored because the CLR provider was configured via either --profile or --providers command.");
                    }
                    else
                    {
                        var clrProvider = Extensions.ToCLREventPipeProvider(clrevents, clreventlevel);
                        providerCollection.Add(clrProvider);
                        enabledBy[Extensions.CLREventProviderName] = "--clrevents";
                    }
                }


                if (providerCollection.Count <= 0)
                {
                    Console.Error.WriteLine("No providers were specified to start a trace.");
                    return ErrorCodes.ArgumentError;
                }

                PrintProviders(providerCollection, enabledBy);

                DiagnosticsClient diagnosticsClient;
                Process process;
                DiagnosticsClientBuilder builder = new DiagnosticsClientBuilder("dotnet-trace", 10);
                bool shouldResumeRuntime = ProcessLauncher.Launcher.HasChildProc || !string.IsNullOrEmpty(diagnosticPort);

                using (DiagnosticsClientHolder holder = await builder.Build(ct, processId, diagnosticPort))
                {
                    diagnosticsClient = holder.Client;
                    if (shouldResumeRuntime)
                    {
                        process = Process.GetProcessById(holder.EndpointInfo.ProcessId);
                    }
                    else
                    {
                        process = Process.GetProcessById(processId);
                    }
                    var shouldExit = new ManualResetEvent(false);
                    var shouldStopAfterDuration = duration != default(TimeSpan);
                    var rundownRequested = false;
                    System.Timers.Timer durationTimer = null;

                    ct.Register(() => shouldExit.Set());

                    using (VirtualTerminalMode vTermMode = VirtualTerminalMode.TryEnable())
                    {
                        EventPipeSession session = null;
                        try
                        {
                            session = diagnosticsClient.StartEventPipeSession(providerCollection, true, (int)buffersize);
                            if (shouldResumeRuntime)
                            {
                                diagnosticsClient.ResumeRuntime();
                            }
                        }
                        catch (DiagnosticsClientException e)
                        {
                            Console.Error.WriteLine($"Unable to start a tracing session: {e.ToString()}");
                        }

                        if (session == null)
                        {
                            Console.Error.WriteLine("Unable to create session.");
                            return ErrorCodes.SessionCreationError;
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
                            Console.Out.WriteLine($"Process        : {process.MainModule.FileName}");
                            Console.Out.WriteLine($"Output File    : {fs.Name}");
                            if (shouldStopAfterDuration)
                                Console.Out.WriteLine($"Trace Duration : {duration.ToString(@"dd\:hh\:mm\:ss")}");
                            Console.Out.WriteLine("\n\n");

                            var fileInfo = new FileInfo(output.FullName);
                            Task copyTask = session.EventStream.CopyToAsync(fs).ContinueWith((task) => shouldExit.Set());

                            if (!Console.IsOutputRedirected)
                            {
                                rewriter = new LineRewriter { LineToClear = Console.CursorTop - 1 };
                                Console.CursorVisible = false;
                            }

                            Action printStatus = () =>
                            {
                                if (!Console.IsOutputRedirected)
                                {
                                    rewriter?.RewriteConsoleLine();
                                    fileInfo.Refresh();
                                    Console.Out.WriteLine($"[{stopwatch.Elapsed.ToString(@"dd\:hh\:mm\:ss")}]\tRecording trace {GetSize(fileInfo.Length)}");
                                    Console.Out.WriteLine("Press <Enter> or <Ctrl+C> to exit...");
                                }

                                if (rundownRequested)
                                    Console.Out.WriteLine("Stopping the trace. This may take up to minutes depending on the application being traced.");
                            };

                            while (!shouldExit.WaitOne(100) && !(!Console.IsInputRedirected && Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter))
                                printStatus();

                            // if the CopyToAsync ended early (target program exited, etc.), the we don't need to stop the session.
                            if (!copyTask.Wait(0))
                            {
                                // Behavior concerning Enter moving text in the terminal buffer when at the bottom of the buffer
                                // is different between Console/Terminals on Windows and Mac/Linux
                                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                                    !Console.IsOutputRedirected &&
                                    rewriter != null &&
                                    Math.Abs(Console.CursorTop - Console.BufferHeight) == 1)
                                {
                                    rewriter.LineToClear--;
                                }
                                durationTimer?.Stop();
                                rundownRequested = true;
                                session.Stop();

                                do
                                {
                                    printStatus();
                                } while (!copyTask.Wait(100));
                            }
                        }

                        Console.Out.WriteLine("\nTrace completed.");

                        if (format != TraceFileFormat.NetTrace)
                            TraceFileFormatConverter.ConvertToFormat(format, output.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                ret = ErrorCodes.TracingError;
            }
            finally
            {
                if (console.GetTerminal() != null)
                    Console.CursorVisible = true;
                
                // If we launched a child proc that hasn't exited yet, terminate it before we exit.
                if (ProcessLauncher.Launcher.HasChildProc && !ProcessLauncher.Launcher.ChildProc.HasExited)
                {
                    ProcessLauncher.Launcher.ChildProc.Kill();
                }
            }
            return await Task.FromResult(ret);
        }

        private static void PrintProviders(IReadOnlyList<EventPipeProvider> providers, Dictionary<string, string> enabledBy)
        {
            Console.Out.WriteLine("");
            Console.Out.Write(String.Format("{0, -40}","Provider Name"));  // +4 is for the tab
            Console.Out.Write(String.Format("{0, -20}","Keywords"));
            Console.Out.Write(String.Format("{0, -20}","Level"));
            Console.Out.Write("Enabled By\r\n");
            foreach (var provider in providers)
            {
                Console.Out.WriteLine(String.Format("{0, -80}", $"{GetProviderDisplayString(provider)}") + $"{enabledBy[provider.Name]}");
            }
            Console.Out.WriteLine();
        }
        private static string GetProviderDisplayString(EventPipeProvider provider) =>
            String.Format("{0, -40}", provider.Name) + String.Format("0x{0, -18}", $"{provider.Keywords:X16}") + String.Format("{0, -8}", provider.EventLevel.ToString() + $"({(int)provider.EventLevel})");

        private static string GetSize(long length)
        {
            if (length > 1e9)
                return String.Format("{0,-8} (GB)", $"{length / 1e9:0.00##}");
            else if (length > 1e6)
                return String.Format("{0,-8} (MB)", $"{length / 1e6:0.00##}");
            else if (length > 1e3)
                return String.Format("{0,-8} (KB)", $"{length / 1e3:0.00##}");
            else
                return String.Format("{0,-8} (B)", $"{length / 1.0:0.00##}");
        }

        public static Command CollectCommand() =>
            new Command(
                name: "collect",
                description: "Collects a diagnostic trace from a currently running process") 
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
            };

        private static uint DefaultCircularBufferSizeInMB() => 256;

        private static Option CircularBufferOption() =>
            new Option(
                alias: "--buffersize",
                description: $"Sets the size of the in-memory circular buffer in megabytes. Default {DefaultCircularBufferSizeInMB()} MB.")
            {
                Argument = new Argument<uint>(name: "size", getDefaultValue: DefaultCircularBufferSizeInMB)
            };

        public static string DefaultTraceName => "trace.nettrace";

        private static Option OutputPathOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: $"The output path for the collected trace data. If not specified it defaults to '{DefaultTraceName}'.")
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
                description: @"The path to a diagnostic port to be created.")
            {
                Argument = new Argument<string>(name: "diagnosticPort", getDefaultValue: () => string.Empty)
            };
    }
}
