// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tools.Common;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal partial class CollectLinuxCommandHandler
    {
        private bool stopTracing;
        private Stopwatch stopwatch = new();
        private LineRewriter rewriter;
        private bool printingStatus;

        internal sealed record CollectLinuxArgs(
            CancellationToken Ct,
            string[] Providers,
            string ClrEventLevel,
            string ClrEvents,
            string[] PerfEvents,
            string[] Profiles,
            FileInfo Output,
            TimeSpan Duration);

        public CollectLinuxCommandHandler(IConsole console = null)
        {
            Console = console ?? new DefaultConsole(false);
            rewriter = new LineRewriter(Console);
        }

        /// <summary>
        /// Collects diagnostic traces using perf_events, a Linux OS technology. collect-linux requires admin privileges to capture kernel- and user-mode events, and by default, captures events from all processes.
        /// This Linux-only command includes the same .NET events as dotnet-trace collect, and it uses the kernel’s user_events mechanism to emit .NET events as perf events, enabling unification of user-space .NET events with kernel-space system events.
        /// </summary>
        internal int CollectLinux(CollectLinuxArgs args)
        {
            if (!OperatingSystem.IsLinux())
            {
                Console.Error.WriteLine("The collect-linux command is only supported on Linux.");
                return (int)ReturnCode.PlatformNotSupportedError;
            }

            Console.WriteLine("==========================================================================================");
            Console.WriteLine("The collect-linux verb is a new preview feature and relies on an updated version of the");
            Console.WriteLine(".nettrace file format. The latest PerfView release supports these trace files but other");
            Console.WriteLine("ways of using the trace file may not work yet. For more details, see the docs at");
            Console.WriteLine("https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace.");
            Console.WriteLine("==========================================================================================");

            args.Ct.Register(() => stopTracing = true);
            int ret = (int)ReturnCode.TracingError;
            string scriptPath = null;
            try
            {
                Console.CursorVisible = false;
                byte[] command = BuildRecordTraceArgs(args, out scriptPath);

                if (args.Duration != default)
                {
                    System.Timers.Timer durationTimer = new(args.Duration.TotalMilliseconds);
                    durationTimer.Elapsed += (sender, e) =>
                    {
                        durationTimer.Stop();
                        stopTracing = true;
                    };
                    durationTimer.Start();
                }
                stopwatch.Start();
                ret = RecordTraceInvoker(command, (UIntPtr)command.Length, OutputHandler);
            }
            catch (CommandLineErrorException e)
            {
                Console.Error.WriteLine($"[ERROR] {e.Message}");
                ret = (int)ReturnCode.TracingError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex}");
                ret = (int)ReturnCode.TracingError;
            }
            finally
            {
                if (!string.IsNullOrEmpty(scriptPath))
                {
                    try
                    {
                        if (File.Exists(scriptPath))
                        {
                            File.Delete(scriptPath);
                        }
                    } catch { }
                }
            }

            return ret;
        }

        public static Command CollectLinuxCommand()
        {
            Command collectLinuxCommand = new("collect-linux")
            {
                CommonOptions.ProvidersOption,
                CommonOptions.CLREventLevelOption,
                CommonOptions.CLREventsOption,
                PerfEventsOption,
                CommonOptions.ProfileOption,
                CommonOptions.OutputPathOption,
                CommonOptions.DurationOption,
            };
            collectLinuxCommand.TreatUnmatchedTokensAsErrors = true; // collect-linux currently does not support child process tracing.
            collectLinuxCommand.Description = "Collects diagnostic traces using perf_events, a Linux OS technology. collect-linux requires admin privileges to capture kernel- and user-mode events, and by default, captures events from all processes. This Linux-only command includes the same .NET events as dotnet-trace collect, and it uses the kernel’s user_events mechanism to emit .NET events as perf events, enabling unification of user-space .NET events with kernel-space system events.";

            collectLinuxCommand.SetAction((parseResult, ct) => {
                string providersValue = parseResult.GetValue(CommonOptions.ProvidersOption) ?? string.Empty;
                string perfEventsValue = parseResult.GetValue(PerfEventsOption) ?? string.Empty;
                string profilesValue = parseResult.GetValue(CommonOptions.ProfileOption) ?? string.Empty;
                CollectLinuxCommandHandler handler = new();

                int rc = handler.CollectLinux(new CollectLinuxArgs(
                    Ct: ct,
                    Providers: providersValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    ClrEventLevel: parseResult.GetValue(CommonOptions.CLREventLevelOption) ?? string.Empty,
                    ClrEvents: parseResult.GetValue(CommonOptions.CLREventsOption) ?? string.Empty,
                    PerfEvents: perfEventsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Profiles: profilesValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Output: parseResult.GetValue(CommonOptions.OutputPathOption) ?? new FileInfo(CommonOptions.DefaultTraceName),
                    Duration: parseResult.GetValue(CommonOptions.DurationOption)));
                return Task.FromResult(rc);
            });

            return collectLinuxCommand;
        }

        private byte[] BuildRecordTraceArgs(CollectLinuxArgs args, out string scriptPath)
        {
            scriptPath = null;
            List<string> recordTraceArgs = new();

            string[] profiles = args.Profiles;
            if (args.Profiles.Length == 0 && args.Providers.Length == 0 && string.IsNullOrEmpty(args.ClrEvents) && args.PerfEvents.Length == 0)
            {
                Console.WriteLine("No providers, profiles, ClrEvents, or PerfEvents were specified, defaulting to trace profiles 'dotnet-common' + 'cpu-sampling'.");
                profiles = new[] { "dotnet-common", "cpu-sampling" };
            }

            StringBuilder scriptBuilder = new();
            List<EventPipeProvider> providerCollection = ProviderUtils.ComputeProviderConfig(args.Providers, args.ClrEvents, args.ClrEventLevel, profiles, true, "collect-linux", Console);
            foreach (EventPipeProvider provider in providerCollection)
            {
                string providerName = provider.Name;
                string providerNameSanitized = providerName.Replace('-', '_').Replace('.', '_');
                long keywords = provider.Keywords;
                uint eventLevel = (uint)provider.EventLevel;
                IDictionary<string, string> arguments = provider.Arguments;
                if (arguments != null && arguments.Count > 0)
                {
                    scriptBuilder.Append($"set_dotnet_filter_args(\n\t\"{providerName}\"");
                    foreach ((string key, string value) in arguments)
                    {
                        scriptBuilder.Append($",\n\t\"{key}={value}\"");
                    }
                    scriptBuilder.Append($");\n");
                }

                scriptBuilder.Append($"let {providerNameSanitized}_flags = new_dotnet_provider_flags();\n");
                scriptBuilder.Append($"record_dotnet_provider(\"{providerName}\", 0x{keywords:X}, {eventLevel}, {providerNameSanitized}_flags);\n\n");
            }

            List<string> linuxEventLines = new();
            foreach (string profile in profiles)
            {
                Profile traceProfile = ListProfilesCommandHandler.TraceProfiles
                    .FirstOrDefault(p => p.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));

                if (traceProfile != null &&
                    !string.IsNullOrEmpty(traceProfile.VerbExclusivity) &&
                    traceProfile.VerbExclusivity.Equals("collect-linux", StringComparison.OrdinalIgnoreCase))
                {
                    recordTraceArgs.Add(traceProfile.CollectLinuxArgs);
                    linuxEventLines.Add($"{traceProfile.Name,-80}--profile");
                }
            }

            foreach (string perfEvent in args.PerfEvents)
            {
                string[] split = perfEvent.Split(':', 2, StringSplitOptions.TrimEntries);
                if (split.Length != 2 || string.IsNullOrEmpty(split[0]) || string.IsNullOrEmpty(split[1]))
                {
                    throw new CommandLineErrorException($"Invalid perf event specification '{perfEvent}'. Expected format 'provider:event'.");
                }

                string perfProvider = split[0];
                string perfEventName = split[1];
                linuxEventLines.Add($"{perfEvent,-80}--perf-events");
                scriptBuilder.Append($"let {perfEventName} = event_from_tracefs(\"{perfProvider}\", \"{perfEventName}\");\nrecord_event({perfEventName});\n\n");
            }

            if (linuxEventLines.Count > 0)
            {
                Console.WriteLine($"{("Linux Perf Events"),-80}Enabled By");
                foreach (string line in linuxEventLines)
                {
                    Console.WriteLine(line);
                }
            }
            else
            {
                Console.WriteLine("No Linux Perf Events enabled.");
            }
            Console.WriteLine();

            FileInfo resolvedOutput = ResolveOutputPath(args.Output);
            recordTraceArgs.Add($"--out");
            recordTraceArgs.Add(resolvedOutput.FullName);
            Console.WriteLine($"Output File    : {resolvedOutput.FullName}");
            Console.WriteLine();

            string scriptText = scriptBuilder.ToString();
            scriptPath = Path.ChangeExtension(resolvedOutput.FullName, ".script");
            File.WriteAllText(scriptPath, scriptText);

            recordTraceArgs.Add("--script-file");
            recordTraceArgs.Add(scriptPath);

            string options = string.Join(' ', recordTraceArgs);
            return Encoding.UTF8.GetBytes(options);
        }

        private static FileInfo ResolveOutputPath(FileInfo output)
        {
            if (!string.Equals(output.Name, CommonOptions.DefaultTraceName, StringComparison.OrdinalIgnoreCase))
            {
                return output;
            }

            DateTime now = DateTime.Now;
            return new FileInfo($"trace_{now:yyyyMMdd}_{now:HHmmss}.nettrace");
        }

        private int OutputHandler(uint type, IntPtr data, UIntPtr dataLen)
        {
            OutputType ot = (OutputType)type;
            if (dataLen != UIntPtr.Zero && (ulong)dataLen <= int.MaxValue)
            {
                string text = Marshal.PtrToStringUTF8(data, (int)dataLen);
                if (!string.IsNullOrEmpty(text) &&
                    !text.StartsWith("Recording started", StringComparison.OrdinalIgnoreCase))
                {
                    if (ot == OutputType.Error)
                    {
                        Console.Error.WriteLine(text);
                        stopTracing = true;
                    }
                    else
                    {
                        Console.Out.WriteLine(text);
                    }
                }
            }

            if (ot == OutputType.Progress)
            {
                if (printingStatus)
                {
                    rewriter.RewriteConsoleLine();
                }
                else
                {
                    printingStatus = true;
                    rewriter.LineToClear = Console.CursorTop - 1;
                }
                Console.Out.WriteLine($"[{stopwatch.Elapsed:dd\\:hh\\:mm\\:ss}]\tRecording trace.");
                Console.Out.WriteLine("Press <Enter> or <Ctrl-C> to exit...");

                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter)
                {
                    stopTracing = true;
                }
            }

            return stopTracing ? 1 : 0;
        }

        private static readonly Option<string> PerfEventsOption =
            new("--perf-events")
            {
                Description = @"Comma-separated list of perf events (e.g. syscalls:sys_enter_execve,sched:sched_switch)."
            };

        private enum OutputType : uint
        {
            Normal = 0,
            Live = 1,
            Error = 2,
            Progress = 3,
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int recordTraceCallback(
            [In] uint type,
            [In] IntPtr data,
            [In] UIntPtr dataLen);

        [LibraryImport("recordtrace", EntryPoint = "RecordTrace")]
        private static partial int RunRecordTrace(
            byte[] command,
            UIntPtr commandLen,
            recordTraceCallback callback);

#region testing seams
        internal Func<byte[], UIntPtr, recordTraceCallback, int> RecordTraceInvoker { get; set; } = RunRecordTrace;
        internal IConsole Console { get; set; }
#endregion
    }
}
