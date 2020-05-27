// Licensed to the .NET Foundation under one or more agreements.
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
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CollectCommandHandler
    {
        delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration, string clrevents, string clreventlevel, string name);

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
        /// <returns></returns>
        private static async Task<int> Collect(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration, string clrevents, string clreventlevel, string name)
        {
            try
            {
                Debug.Assert(output != null);
                Debug.Assert(profile != null);

                // Either processName or processId has to be specified.
                if (name != null)
                {
                    if (processId != 0)
                    {
                        Console.WriteLine("Can only specify either --name or --process-id option.");
                        return ErrorCodes.ArgumentError;
                    }
                    processId = CommandUtils.FindProcessIdWithName(name);
                    if (processId < 0)
                    {
                        return ErrorCodes.ArgumentError;
                    }
                }

                if (processId < 0)
                {
                    Console.Error.WriteLine("Process ID should not be negative.");
                    return ErrorCodes.ArgumentError;
                }
                else if (processId == 0)
                {
                    Console.Error.WriteLine("--process-id is required");
                    return ErrorCodes.ArgumentError;
                }

                bool hasConsole = console.GetTerminal() != null;

                if (hasConsole)
                    Console.Clear();

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

                var process = Process.GetProcessById(processId);
                var shouldExit = new ManualResetEvent(false);
                var shouldStopAfterDuration = duration != default(TimeSpan);
                var failed = false;
                var terminated = false;
                var rundownRequested = false;
                System.Timers.Timer durationTimer = null;

                ct.Register(() => shouldExit.Set());

                var diagnosticsClient = new DiagnosticsClient(processId);
                using (VirtualTerminalMode vTermMode = VirtualTerminalMode.TryEnable())
                {
                    EventPipeSession session = null;
                    try
                    {
                        session = diagnosticsClient.StartEventPipeSession(providerCollection, true);
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

                    var collectingTask = new Task(() =>
                    {
                        try
                        {
                            var stopwatch = new Stopwatch();
                            durationTimer?.Start();
                            stopwatch.Start();

                            using (var fs = new FileStream(output.FullName, FileMode.Create, FileAccess.Write))
                            {
                                Console.Out.WriteLine($"Process        : {process.MainModule.FileName}");
                                Console.Out.WriteLine($"Output File    : {fs.Name}");
                                if (shouldStopAfterDuration)
                                    Console.Out.WriteLine($"Trace Duration : {duration.ToString(@"dd\:hh\:mm\:ss")}");

                                Console.Out.WriteLine("\n\n");
                                var buffer = new byte[16 * 1024];

                                while (true)
                                {
                                    int nBytesRead = session.EventStream.Read(buffer, 0, buffer.Length);
                                    if (nBytesRead <= 0)
                                        break;
                                    fs.Write(buffer, 0, nBytesRead);

                                    if (!rundownRequested)
                                    {
                                        if (hasConsole)
                                        {
                                            lineToClear = Console.CursorTop - 1;
                                            ResetCurrentConsoleLine(vTermMode.IsEnabled);
                                        }

                                        Console.Out.WriteLine($"[{stopwatch.Elapsed.ToString(@"dd\:hh\:mm\:ss")}]\tRecording trace {GetSize(fs.Length)}");
                                        Console.Out.WriteLine("Press <Enter> or <Ctrl+C> to exit...");
                                        Debug.WriteLine($"PACKET: {Convert.ToBase64String(buffer, 0, nBytesRead)} (bytes {nBytesRead})");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failed = true;
                            Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                        }
                        finally
                        {
                            terminated = true;
                            shouldExit.Set();
                        }
                    });
                    collectingTask.Start();

                    do
                    {
                        while (!Console.KeyAvailable && !shouldExit.WaitOne(250)) { }
                    } while (!shouldExit.WaitOne(0) && Console.ReadKey(true).Key != ConsoleKey.Enter);

                    if (!terminated)
                    {
                        durationTimer?.Stop();
                        if (hasConsole)
                        {
                            lineToClear = Console.CursorTop;
                            ResetCurrentConsoleLine(vTermMode.IsEnabled);
                        }
                        Console.Out.WriteLine("Stopping the trace. This may take up to minutes depending on the application being traced.");
                        rundownRequested = true;
                        session.Stop();
                    }
                    await collectingTask;
                }

                Console.Out.WriteLine();
                Console.Out.WriteLine("Trace completed.");

                if (format != TraceFileFormat.NetTrace)
                    TraceFileFormatConverter.ConvertToFormat(format, output.FullName);

                return failed ? ErrorCodes.TracingError : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return ErrorCodes.UnknownError;
            }
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

        private static int prevBufferWidth = 0;
        private static string clearLineString = "";
        private static int lineToClear = 0;

        private static void ResetCurrentConsoleLine(bool isVTerm)
        {
            if (isVTerm)
            {
                // ANSI escape codes:
                //  [2K => clear current line
                //  [{lineToClear};0H => move cursor to column 0 of row `lineToClear`
                Console.Out.Write($"\u001b[2K\u001b[{lineToClear};0H");
            }
            else
            {
                if (prevBufferWidth != Console.BufferWidth)
                {
                    prevBufferWidth = Console.BufferWidth;
                    clearLineString = new string(' ', Console.BufferWidth - 1);
                }
                Console.SetCursorPosition(0, lineToClear);
                Console.Out.Write(clearLineString);
                Console.SetCursorPosition(0, lineToClear);
            }
        }

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
                CommonOptions.NameOption()
            };

        private static uint DefaultCircularBufferSizeInMB => 256;

        private static Option CircularBufferOption() =>
            new Option(
                alias: "--buffersize",
                description: $"Sets the size of the in-memory circular buffer in megabytes. Default {DefaultCircularBufferSizeInMB} MB.")
            {
                Argument = new Argument<uint>(name: "size", defaultValue: DefaultCircularBufferSizeInMB)
            };

        public static string DefaultTraceName => "trace.nettrace";

        private static Option OutputPathOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: $"The output path for the collected trace data. If not specified it defaults to '{DefaultTraceName}'.")
            {
                Argument = new Argument<FileInfo>(name: "trace-file-path", defaultValue: new FileInfo(DefaultTraceName))
            };

        private static Option ProvidersOption() =>
            new Option(
                alias: "--providers",
                description: @"A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'. These providers are in addition to any providers implied by the --profile argument. If there is any discrepancy for a particular provider, the configuration here takes precedence over the implicit configuration from the profile.")
            {
                Argument = new Argument<string>(name: "list-of-comma-separated-providers", defaultValue: "") // TODO: Can we specify an actual type?
            };

        private static Option ProfileOption() =>
            new Option(
                alias: "--profile",
                description: @"A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.")
            {
                Argument = new Argument<string>(name: "profile-name", defaultValue: "")
            };

        private static Option DurationOption() =>
            new Option(
                alias: "--duration",
                description: @"When specified, will trace for the given timespan and then automatically stop the trace. Provided in the form of dd:hh:mm:ss.")
            {
                Argument = new Argument<TimeSpan>(name: "duration-timespan", defaultValue: default),
                IsHidden = true
            };
        
        private static Option CLREventsOption() => 
            new Option(
                alias: "--clrevents",
                description: @"List of CLR runtime events to emit.")
            {
                Argument = new Argument<string>(name: "clrevents", defaultValue: "")
            };

        private static Option CLREventLevelOption() => 
            new Option(
                alias: "--clreventlevel",
                description: @"Verbosity of CLR events to be emitted.")
            {
                Argument = new Argument<string>(name: "clreventlevel", defaultValue: "")
            };
    }
}
