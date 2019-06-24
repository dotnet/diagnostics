// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.CommandLine;
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
        /// <summary>
        /// Collects a diagnostic trace from a currently running process.
        /// </summary>
        /// <param name="console"></param>
        /// <param name="processId">The process to collect the trace from.</param>
        /// <param name="output">The output path for the collected trace data.</param>
        /// <param name="buffersize">Sets the size of the in-memory circular buffer in megabytes.</param>
        /// <param name="providers">A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: '(GUID|KnownProviderName)[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'</param>
        /// <param name="profile">A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.</param>
        /// <param name="format">The desired format of the created trace file.</param>
        /// <returns></returns>
        private static async Task<int> Collect(IConsole console, int processId, FileInfo output, uint buffersize, string providers, string profile, TraceFileFormat format)
        {
            try
            {
                Debug.Assert(output != null);
                Debug.Assert(profile != null);
                if (processId <= 0)
                {
                    Console.Error.WriteLine("Process ID should not be negative.");
                    return ErrorCodes.ArgumentError;
                }

                var selectedProfile = ListProfilesCommandHandler.DotNETRuntimeProfiles
                    .FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));
                if (selectedProfile == null)
                {
                    Console.Error.WriteLine($"Invalid profile name: {profile}");
                    return ErrorCodes.ArgumentError;
                }

                var providerCollection = Extensions.ToProviders(providers);
                var profileProviders = new List<Provider>();

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
                            profileProviders.Add(selectedProfileProvider);
                        }
                    }
                }

                providerCollection.AddRange(profileProviders);

                if (providerCollection.Count <= 0)
                {
                    Console.Error.WriteLine("No providers were specified to start a trace.");
                    return ErrorCodes.ArgumentError;
                }

                PrintProviders(providerCollection);

                var process = Process.GetProcessById(processId);
                var configuration = new SessionConfiguration(
                    circularBufferSizeMB: buffersize,
                    format: EventPipeSerializationFormat.NetTrace,
                    providers: providerCollection);

                var shouldExit = new ManualResetEvent(false);
                var failed = false;
                var terminated = false;

                ulong sessionId = 0;
                using (Stream stream = EventPipeClient.CollectTracing(processId, configuration, out sessionId))
                using (VirtualTerminalMode vTermMode = VirtualTerminalMode.TryEnable())
                {
                    if (sessionId == 0)
                    {
                        Console.Error.WriteLine("Unable to create session.");
                        return ErrorCodes.SessionCreationError;
                    }
                    if (File.Exists(output.FullName))
                    {
                        Console.Error.WriteLine("Unable to create file.");
                        return ErrorCodes.FileCreationError;
                    }
                    var collectingTask = new Task(() => {
                        try
                        {
                            using (var fs = new FileStream(output.FullName, FileMode.Create, FileAccess.Write))
                            {
                                Console.Out.WriteLine($"Process     : {process.MainModule.FileName}");
                                Console.Out.WriteLine($"Output File : {fs.Name}");
                                Console.Out.WriteLine($"\tSession Id: 0x{sessionId:X16}");
                                lineToClear = Console.CursorTop;

                                while (true)
                                {
                                    var buffer = new byte[16 * 1024];
                                    int nBytesRead = stream.Read(buffer, 0, buffer.Length);
                                    if (nBytesRead <= 0)
                                        break;
                                    fs.Write(buffer, 0, nBytesRead);

                                    ResetCurrentConsoleLine(vTermMode.IsEnabled);
                                    Console.Out.Write($"\tRecording trace {GetSize(fs.Length)}");

                                    Debug.WriteLine($"PACKET: {Convert.ToBase64String(buffer, 0, nBytesRead)} (bytes {nBytesRead})");
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

                    Console.Out.WriteLine("Press <Enter> or <Ctrl+C> to exit...");
                    Console.CancelKeyPress += (sender, args) => {
                        args.Cancel = true;
                        shouldExit.Set();
                    };

                    do {
                        while (!Console.KeyAvailable && !shouldExit.WaitOne(250)) { }
                    } while (!shouldExit.WaitOne(0) && Console.ReadKey(true).Key != ConsoleKey.Enter);

                    if (!terminated)
                    {
                        EventPipeClient.StopTracing(processId, sessionId);
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

        [Conditional("DEBUG")]
        private static void PrintProviders(IReadOnlyList<Provider> providers)
        {
            Console.Out.WriteLine("Enabling the following providers");
            foreach (var provider in providers)
                Console.Out.WriteLine($"\t{provider.ToString()}");
        }

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
                Console.SetCursorPosition(0,lineToClear);
                Console.Out.Write(clearLineString);
                Console.SetCursorPosition(0,lineToClear);
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
                return String.Format("{0,-8} (byte)", $"{length / 1.0:0.00##}");
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
                },
                handler: System.CommandLine.Invocation.CommandHandler.Create<IConsole, int, FileInfo, uint, string, string, TraceFileFormat>(Collect));

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
                description: @"A list of EventPipe providers to be enabled. This is in the form 'Provider[,Provider]', where Provider is in the form: '(GUID|KnownProviderName)[:Flags[:Level][:KeyValueArgs]]', and KeyValueArgs is in the form: '[key1=value1][;key2=value2]'",
                argument: new Argument<string>(defaultValue: "") { Name = "list-of-comma-separated-providers" }, // TODO: Can we specify an actual type?
                isHidden: false);

        private static Option ProfileOption() =>
            new Option(
                alias: "--profile",
                description: @"A named pre-defined set of provider configurations that allows common tracing scenarios to be specified succinctly.",
                argument: new Argument<string>(defaultValue: "runtime-basic") { Name = "profile_name" },
                isHidden: false);
    }
}
