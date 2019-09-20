// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CollectCommandHandler
    {
        delegate Task<int> CollectDelegate(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration);

        /// <summary>
        /// Collects a diagnostic trace from a currently running process.
        /// </summary>
        /// <param name="ct">The cancellation token</param>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the trace from.</param>
        /// <param name="output">The output path for the collected trace data.</param>
        /// <param name="buffersize">Sets the size of the in-memory circular buffer in megabytes.</param>
        /// <param name="providers">A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'</param>
        /// <param name="profile">A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.</param>
        /// <param name="format">The desired format of the created trace file.</param>
        /// <returns></returns>
        private static async Task<int> Collect(CancellationToken ct, IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format, TimeSpan duration)
        {
            try
            {
                using (VirtualTerminalMode vTermMode = VirtualTerminalMode.TryEnable())
                using (var systemConsole = new SystemConsoleTerminal(console))
                using (var screenView = new ScreenView(new ConsoleRenderer(systemConsole, resetAfterRender: true)))
                {
                    
                    var region = new Region(0, 0, Console.WindowWidth, Console.WindowHeight, true);
                    var parentStackView = new StackLayoutView(Orientation.Vertical);
                    screenView.Child = parentStackView;
                    var completionObservable = new BehaviorSubject<string>("");

                    Debug.Assert(output != null);
                    Debug.Assert(profile != null);
                    systemConsole.Clear();
                    if (processId <= 0)
                    {
                        systemConsole.Error.WriteLine("Process ID should not be negative.");
                        return ErrorCodes.ArgumentError;
                    }

                    if (profile.Length == 0 && providers.Length == 0)
                    {
                        parentStackView.Add(new ContentView("No profile or providers specified, defaulting to trace profile 'cpu-sampling'"));
                        profile = "cpu-sampling";
                    }

                    Dictionary<string, string> enabledBy = new Dictionary<string, string>();

                    var providerCollection = Extensions.ToProviders(providers);
                    foreach (Provider providerCollectionProvider in providerCollection)
                    {
                        enabledBy[providerCollectionProvider.Name] = "--providers ";
                    }

                    if (profile.Length != 0)
                    {
                        var profileProviders = new List<Provider>();
                        var selectedProfile = ListProfilesCommandHandler.DotNETRuntimeProfiles
                            .FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));
                        if (selectedProfile == null)
                        {
                            Console.Error.WriteLine($"Invalid profile name: {profile}");
                            return ErrorCodes.ArgumentError;
                        }

                        // If user defined a different key/level on the same provider via --providers option that was specified via --profile option,
                        // --providers option takes precedence. Go through the list of providers specified and only add it if it wasn't specified
                        // via --providers options.
                        if (selectedProfile.Providers != null)
                        {
                            foreach (Provider selectedProfileProvider in selectedProfile.Providers)
                            {
                                bool shouldAdd = true;

                                foreach (Provider providerCollectionProvider in providerCollection)
                                {
                                    if (providerCollectionProvider.Name.Equals(selectedProfileProvider.Name))
                                    {
                                        shouldAdd = false;
                                        break;
                                    }
                                }

                                if (shouldAdd)
                                {
                                    enabledBy[selectedProfileProvider.Name] = "--profile ";
                                    profileProviders.Add(selectedProfileProvider);
                                }
                            }
                        }
                        providerCollection.AddRange(profileProviders);
                    }


                    if (providerCollection.Count <= 0)
                    {
                        systemConsole.Error.WriteLine("No providers were specified to start a trace.");
                        return ErrorCodes.ArgumentError;
                    }

                    PrintProviders(providerCollection, enabledBy, parentStackView);

                    var process = Process.GetProcessById(processId);
                    var configuration = new SessionConfiguration(
                        circularBufferSizeMB: buffersize,
                        format: EventPipeSerializationFormat.NetTrace,
                        providers: providerCollection);

                    var shouldExit = new ManualResetEvent(false);
                    var shouldStopAfterDuration = duration != default(TimeSpan);
                    var failed = false;
                    var terminated = false;
                    System.Timers.Timer durationTimer = null;

                    ct.Register(() => shouldExit.Set());

                    ulong sessionId = 0;
                    using (Stream stream = EventPipeClient.CollectTracing(processId, configuration, out sessionId))
                    {
                        if (sessionId == 0)
                        {
                            systemConsole.Error.WriteLine("Unable to create session.");
                            return ErrorCodes.SessionCreationError;
                        }

                        if (shouldStopAfterDuration)
                        {
                            durationTimer = new System.Timers.Timer(duration.TotalMilliseconds);
                            durationTimer.Elapsed += (s, e) => shouldExit.Set();
                            durationTimer.AutoReset = false;
                        }

                        parentStackView.Add(new ContentView($"Process        : {process.MainModule.FileName}"));
                        parentStackView.Add(new ContentView($"Output File    : {output.Name}"));
                        if (shouldStopAfterDuration)
                            parentStackView.Add(new ContentView($"Trace Duration : {duration.ToString(@"dd\:hh\:mm\:ss")}"));

                        parentStackView.Add(new ContentView(""));

                        var horizontalStackView = new StackLayoutView(Orientation.Horizontal);

                        var timeObservable = new BehaviorSubject<TimeSpan>(default(TimeSpan));
                        var timeView = ContentView.FromObservable(timeObservable, ts => $"[{ts.ToString(@"dd\:hh\:mm\:ss")}]\t");

                        var fileSizeObservable = new BehaviorSubject<long>(0);
                        var fileSizeView = ContentView.FromObservable(fileSizeObservable, fs => $"Recording trace {GetSize(fs)}");

                        horizontalStackView.Add(timeView);
                        horizontalStackView.Add(fileSizeView);
                        parentStackView.Add(horizontalStackView);
                        parentStackView.Add(new ContentView("Press <Enter> or <Ctrl+C> to exit..."));

                        var completionView = ContentView.FromObservable(completionObservable);
                        parentStackView.Add(completionView);

                        // Must happen _after_ all data has been added to children views
                        screenView.Render(region);

                        var collectingTask = new Task(() =>
                        {
                            try
                            {
                                var stopwatch = new Stopwatch();
                                durationTimer?.Start();
                                stopwatch.Start();

                                using (var fs = new FileStream(output.FullName, FileMode.Create, FileAccess.Write))
                                {
                                    var buffer = new byte[16 * 1024];

                                    Task.Run(() =>
                                    {
                                        var prevTime = stopwatch.Elapsed;
                                        while (!terminated)
                                        {
                                            if (stopwatch.Elapsed - prevTime > TimeSpan.FromSeconds(1))
                                            {
                                                prevTime = stopwatch.Elapsed;
                                                timeObservable.OnNext(stopwatch.Elapsed);
                                            }
                                        }
                                    });

                                    while (true)
                                    {
                                        int nBytesRead = stream.Read(buffer, 0, buffer.Length);
                                        if (nBytesRead <= 0)
                                            break;
                                        fs.Write(buffer, 0, nBytesRead);
                                        fileSizeObservable.OnNext(fs.Length);
                                        Debug.WriteLine($"PACKET: {Convert.ToBase64String(buffer, 0, nBytesRead)} (bytes {nBytesRead})");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                failed = true;
                                systemConsole.Error.WriteLine($"[ERROR] {ex.ToString()}");
                            }
                            finally
                            {
                                terminated = true;
                                shouldExit.Set();
                            }
                        });
                        collectingTask.Start();

                        if (systemConsole.IsInputRedirected)
                        {
                            var exitCharacters = new char[] { 'q', '\r', '\n' };
                            // if stdin is redirected, look for 'q', '\r', or '\n' to indicate we should stop
                            // tracing.  Generally, we would expect people to use the runtime client
                            // library directly or the hidden duration flag rather than this option...
                            //
                            // Need to use In.Peek() and In.Read() because KeyAvailable and ReadKey
                            // don't work under redirection.
                            do
                            {
                            while (Console.In.Peek() == -1 && !shouldExit.WaitOne(250)) { }
                            } while (!shouldExit.WaitOne(0) && !exitCharacters.Contains((char)Console.In.Read()));
                        }
                        else
                        {
                            do
                            {
                                while (!Console.KeyAvailable && !shouldExit.WaitOne(250)) { }
                            } while (!shouldExit.WaitOne(0) && Console.ReadKey(true).Key != ConsoleKey.Enter);
                        }

                        if (!terminated)
                        {
                            durationTimer?.Stop();
                            EventPipeClient.StopTracing(processId, sessionId);
                        }
                        await collectingTask;
                    }

                    var completionText = "Trace completed.";
                    completionObservable.OnNext(completionText);
                    Action<string> postCompletionLogger = message => 
                    {
                        completionText += $"\n{message}";
                        completionObservable.OnNext(completionText);
                    };

                    if (format != TraceFileFormat.NetTrace)
                        TraceFileFormatConverter.ConvertToFormat(format, output.FullName, logger: postCompletionLogger);

                    return failed ? ErrorCodes.TracingError : 0;
                }
            }
            catch (Exception ex)
            {
                console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return ErrorCodes.UnknownError;
            }
        }

        private static void PrintProviders(IReadOnlyList<Provider> providers, Dictionary<string, string> enabledBy, StackLayoutView stackView = null)
        {
            var providerTableView = new TableView<Provider>();
            providerTableView.AddColumn(p => p.Name, "Provider Name", ColumnDefinition.SizeToContent());
            providerTableView.AddColumn(p => $"{p.Keywords:X16}", "Keywords", ColumnDefinition.SizeToContent());
            providerTableView.AddColumn(p => $"{p.EventLevel.ToString()}({(int)p.EventLevel})", "Level", ColumnDefinition.SizeToContent());
            providerTableView.Items = providers;

            stackView.Add(new ContentView(""));
            stackView.Add(providerTableView);
            stackView.Add(new ContentView(""));
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
                description: "Collects a diagnostic trace from a currently running process",
                symbols: new Option[] {
                    CommonOptions.ProcessIdOption(),
                    CircularBufferOption(),
                    OutputPathOption(),
                    ProvidersOption(),
                    ProfileOption(),
                    CommonOptions.FormatOption(),
                    DurationOption()
                },
                handler: HandlerDescriptor.FromDelegate((CollectDelegate)Collect).GetCommandHandler());

        private static uint DefaultCircularBufferSizeInMB => 256;

        private static Option CircularBufferOption() =>
            new Option(
                alias: "--buffersize",
                description: $"Sets the size of the in-memory circular buffer in megabytes. Default {DefaultCircularBufferSizeInMB} MB.",
                argument: new Argument<uint>(defaultValue: DefaultCircularBufferSizeInMB) { Name = "size" },
                isHidden: false);

        public static string DefaultTraceName => "trace.nettrace";

        private static Option OutputPathOption() =>
            new Option(
                aliases: new[] { "-o", "--output" },
                description: $"The output path for the collected trace data. If not specified it defaults to '{DefaultTraceName}'",
                argument: new Argument<FileInfo>(defaultValue: new FileInfo(DefaultTraceName)) { Name = "trace-file-path" },
                isHidden: false);

        private static Option ProvidersOption() =>
            new Option(
                alias: "--providers",
                description: @"A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: 'KnownProviderName[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'. These providers are in addition to any providers implied by the --profile argument. If there is any discrepancy for a particular provider, the configuration here takes precedence over the implicit configuration from the profile.",
                argument: new Argument<string>(defaultValue: "") { Name = "list-of-comma-separated-providers" }, // TODO: Can we specify an actual type?
                isHidden: false);

        private static Option ProfileOption() =>
            new Option(
                alias: "--profile",
                description: @"A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.",
                argument: new Argument<string>(defaultValue: "") { Name = "profile-name" },
                isHidden: false);

        private static Option DurationOption() =>
            new Option(
                alias: "--duration",
                description: @"When specified, will trace for the given timespan and then automatically stop the trace. Provided in the form of dd:hh:mm:ss.",
                argument: new Argument<TimeSpan>(defaultValue: default(TimeSpan)) { Name = "duration-timespan" },
                isHidden: true);
    }
}
