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
    internal static class CommandHelpers
    {
        public static async Task<int> Trace(
            CancellationToken ct, 
            IConsole console, 
            int processId, 
            uint buffersize, 
            string providers, 
            string profile, 
            TimeSpan duration,
            Action onBeforeStart,
            Action<TraceCommandInfo> onStart,
            Action onSuccess)
        {
            try
            {
                onBeforeStart();
                Debug.Assert(profile != null);

                bool hasConsole = console.GetTerminal() != null;

                if (hasConsole)
                    Console.Clear();

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

                if (profile.Length == 0 && providers.Length == 0)
                {
                    Console.Out.WriteLine("No profile or providers specified, defaulting to trace profile 'cpu-sampling'");
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
                    var selectedProfile = ListProfilesCommandHandler.DotNETRuntimeProfiles
                        .FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));
                    if (selectedProfile == null)
                    {
                        Console.Error.WriteLine($"Invalid profile name: {profile}");
                        return ErrorCodes.ArgumentError;
                    }

                    Profile.MergeProfileAndProviders(selectedProfile, providerCollection, enabledBy);
                }

                if (providerCollection.Count <= 0)
                {
                    Console.Error.WriteLine("No providers were specified to start a trace.");
                    return ErrorCodes.ArgumentError;
                }

                PrintProviders(providerCollection, enabledBy);

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
                using (VirtualTerminalMode vTermMode = VirtualTerminalMode.TryEnable())
                {
                    if (sessionId == 0)
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

                            onStart(
                                new TraceCommandInfo(
                                    stream, 
                                    process.MainModule.FileName, 
                                    shouldStopAfterDuration, 
                                    hasConsole, 
                                    vTermMode.IsEnabled,
                                    stopwatch.Elapsed));
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
                        EventPipeClient.StopTracing(processId, sessionId);
                    }
                    await collectingTask;
                }

                onSuccess();

                return failed ? ErrorCodes.TracingError : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.ToString()}");
                return ErrorCodes.UnknownError;
            }
        }

        private static void PrintProviders(IReadOnlyList<Provider> providers, IReadOnlyDictionary<string, string> enabledBy)
        {
            Console.Out.WriteLine("");
            Console.Out.Write(String.Format("{0, -40}", "Provider Name"));  // +4 is for the tab
            Console.Out.Write(String.Format("{0, -20}", "Keywords"));
            Console.Out.Write(String.Format("{0, -20}", "Level"));
            Console.Out.Write("Enabled By\n");
            foreach (var provider in providers)
            {
                Console.Out.WriteLine(String.Format("{0, -80}", $"{provider.ToDisplayString()}") + $"{enabledBy[provider.Name]}");
            }
            Console.Out.WriteLine();
        }
    }

    internal sealed class TraceCommandInfo
    {
        public TraceCommandInfo(
            Stream eventPipeStream, 
            string processFileName, 
            bool shouldStopAfterDuration, 
            bool hasConsole, 
            bool isVTermEnabled,
            TimeSpan elapsed)
        {
            EventPipeStream = eventPipeStream;
            ProcessFileName = processFileName;
            ShouldStopAfterDuration = shouldStopAfterDuration;
            HasConsole = hasConsole;
            IsVirtualTerminalModeEnabled = isVTermEnabled;
            Elapsed = elapsed;
        }

        public Stream EventPipeStream { get; }

        public string ProcessFileName { get; }

        public bool ShouldStopAfterDuration { get; }

        public bool HasConsole { get; }

        public bool IsVirtualTerminalModeEnabled { get; }

        public TimeSpan Elapsed { get; }
    }
}
