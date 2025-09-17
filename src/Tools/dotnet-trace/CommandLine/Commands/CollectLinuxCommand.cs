// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static partial class CollectLinuxCommandHandler
    {
        private static int s_recordStatus;

        internal sealed record CollectLinuxArgs(
            string[] Providers,
            string ClrEventLevel,
            string ClrEvents,
            string[] PerfEvents,
            string[] Profiles,
            FileInfo Output,
            TimeSpan Duration,
            string Name,
            int ProcessId);

        /// <summary>
        /// Collects diagnostic traces using perf_events, a Linux OS technology. collect-linux requires admin privileges to capture kernel- and user-mode events, and by default, captures events from all processes.
        /// This Linux-only command includes the same .NET events as dotnet-trace collect, and it uses the kernel’s user_events mechanism to emit .NET events as perf events, enabling unification of user-space .NET events with kernel-space system events.
        /// </summary>
        private static int CollectLinux(CollectLinuxArgs args)
        {
            if (!OperatingSystem.IsLinux())
            {
                Console.Error.WriteLine("The collect-linux command is only supported on Linux.");
                return (int)ReturnCode.ArgumentError;
            }

            if (args.ProcessId != 0 && !string.IsNullOrEmpty(args.Name))
            {
                Console.Error.WriteLine("Only one of --process-id or --name can be specified.");
                return (int)ReturnCode.ArgumentError;
            }

            return RunRecordTrace(args);
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
                CommonOptions.NameOption,
                CommonOptions.ProcessIdOption
            };
            collectLinuxCommand.TreatUnmatchedTokensAsErrors = true; // collect-linux currently does not support child process tracing.
            collectLinuxCommand.Description = "Collects diagnostic traces using perf_events, a Linux OS technology. collect-linux requires admin privileges to capture kernel- and user-mode events, and by default, captures events from all processes. This Linux-only command includes the same .NET events as dotnet-trace collect, and it uses the kernel’s user_events mechanism to emit .NET events as perf events, enabling unification of user-space .NET events with kernel-space system events.";

            collectLinuxCommand.SetAction((parseResult, ct) => {
                string providersValue = parseResult.GetValue(CommonOptions.ProvidersOption) ?? string.Empty;
                string perfEventsValue = parseResult.GetValue(PerfEventsOption) ?? string.Empty;
                string profilesValue = parseResult.GetValue(CommonOptions.ProfileOption) ?? string.Empty;

                int rc = CollectLinux(new CollectLinuxArgs(
                    Providers: providersValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    ClrEventLevel: parseResult.GetValue(CommonOptions.CLREventLevelOption) ?? string.Empty,
                    ClrEvents: parseResult.GetValue(CommonOptions.CLREventsOption) ?? string.Empty,
                    PerfEvents: perfEventsValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Profiles: profilesValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Output: parseResult.GetValue(CommonOptions.OutputPathOption) ?? new FileInfo(CommonOptions.DefaultTraceName),
                    Duration: parseResult.GetValue(CommonOptions.DurationOption),
                    Name: parseResult.GetValue(CommonOptions.NameOption) ?? string.Empty,
                    ProcessId: parseResult.GetValue(CommonOptions.ProcessIdOption)));
                return Task.FromResult(rc);
            });

            return collectLinuxCommand;
        }

        private static int RunRecordTrace(CollectLinuxArgs args)
        {
            s_recordStatus = 0;

            ConsoleCancelEventHandler handler = (sender, e) =>
            {
                e.Cancel = true;
                s_recordStatus = 1;
            };
            Console.CancelKeyPress += handler;

            IEnumerable<string> recordTraceArgList = BuildRecordTraceArgs(args, out string scriptPath);

            string options = string.Join(' ', recordTraceArgList);
            byte[] command = Encoding.UTF8.GetBytes(options);
            int rc;
            try
            {
                rc = RecordTrace(command, (UIntPtr)command.Length, OutputHandler);
            }
            finally
            {
                Console.CancelKeyPress -= handler;
                if (!string.IsNullOrEmpty(scriptPath))
                {
                    try {
                        if (File.Exists(scriptPath))
                        {
                            File.Delete(scriptPath);
                        }
                    } catch { }
                }
            }

            return rc;
        }

        private static List<string> BuildRecordTraceArgs(CollectLinuxArgs args, out string scriptPath)
        {
            scriptPath = null;
            List<string> recordTraceArgs = new();

            foreach (string profile in args.Profiles)
            {
                if (profile.Equals("kernel-cpu", StringComparison.OrdinalIgnoreCase))
                {
                    recordTraceArgs.Add("--on-cpu");
                }
                if (profile.Equals("kernel-cswitch", StringComparison.OrdinalIgnoreCase))
                {
                    recordTraceArgs.Add("--off-cpu");
                }
            }

            int pid = args.ProcessId;
            if (!string.IsNullOrEmpty(args.Name))
            {
                pid = CommandUtils.FindProcessIdWithName(args.Name);
            }
            if (pid > 0)
            {
                recordTraceArgs.Add($"--pid");
                recordTraceArgs.Add($"{pid}");
            }

            string resolvedOutput = ResolveOutputPath(args.Output, pid);
            recordTraceArgs.Add($"--out");
            recordTraceArgs.Add(resolvedOutput);

            if (args.Duration != default(TimeSpan))
            {
                recordTraceArgs.Add($"--duration");
                recordTraceArgs.Add(args.Duration.ToString());
            }

            StringBuilder scriptBuilder = new();

            string[] profiles = args.Profiles;
            if (args.Profiles.Length == 0 && args.Providers.Length == 0 && string.IsNullOrEmpty(args.ClrEvents))
            {
                Console.WriteLine("No profile or providers specified, defaulting to trace profile 'dotnet-common'");
                profiles = new[] { "dotnet-common" };
            }

            List<EventPipeProvider> providerCollection = ProviderUtils.ToProviders(args.Providers, args.ClrEvents, args.ClrEventLevel, profiles, true);
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

            foreach (string perfEvent in args.PerfEvents)
            {
                if (string.IsNullOrWhiteSpace(perfEvent) || !perfEvent.Contains(':', StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Invalid perf event specification '{perfEvent}'. Expected format 'provider:event'.");
                }

                string[] split = perfEvent.Split(':', 2, StringSplitOptions.TrimEntries);
                if (split.Length != 2 || string.IsNullOrEmpty(split[0]) || string.IsNullOrEmpty(split[1]))
                {
                    throw new ArgumentException($"Invalid perf event specification '{perfEvent}'. Expected format 'provider:event'.");
                }

                string perfProvider = split[0];
                string perfEventName = split[1];
                scriptBuilder.Append($"let {perfEventName} = event_from_tracefs(\"{perfProvider}\", \"{perfEventName}\");\nrecord_event({perfEventName});\n\n");
            }

            string scriptText = scriptBuilder.ToString();
            string scriptFileName = $"{Path.GetFileNameWithoutExtension(resolvedOutput)}.script";
            scriptPath = Path.Combine(Environment.CurrentDirectory, scriptFileName);
            File.WriteAllText(scriptPath, scriptText);

            recordTraceArgs.Add("--script-file");
            recordTraceArgs.Add(scriptPath);

            return recordTraceArgs;
        }

        private static string ResolveOutputPath(FileInfo output, int processId)
        {
            if (!string.Equals(output.Name, CommonOptions.DefaultTraceName, StringComparison.OrdinalIgnoreCase))
            {
                return output.Name;
            }

            DateTime now = DateTime.Now;
            if (processId > 0)
            {
                Process process = Process.GetProcessById(processId);
                FileInfo processMainModuleFileInfo = new(process.MainModule.FileName);
                return $"{processMainModuleFileInfo.Name}_{now:yyyyMMdd}_{now:HHmmss}.nettrace";
            }

            return $"collect_linux_{now:yyyyMMdd}_{now:HHmmss}.nettrace";
        }

        private static int OutputHandler(uint type, IntPtr data, UIntPtr dataLen)
        {
            OutputType ot = (OutputType)type;
            if (ot != OutputType.Progress)
            {
                int len = checked((int)dataLen);
                if (len > 0)
                {
                    byte[] buffer = new byte[len];
                    Marshal.Copy(data, buffer, 0, len);
                    string text = Encoding.UTF8.GetString(buffer);
                    switch (ot)
                    {
                    case OutputType.Normal:
                    case OutputType.Live:
                        Console.Out.WriteLine(text);
                        break;
                    case OutputType.Error:
                        Console.Error.WriteLine(text);
                        break;
                    default:
                        Console.Error.WriteLine($"[{ot}] {text}");
                        break;
                    }
                }
            }

            return s_recordStatus;
        }

        private static readonly Option<string> PerfEventsOption =
            new("--perf-events")
            {
                Description = @"Comma-separated list of kernel perf events (e.g. syscalls:sys_enter_execve,sched:sched_switch)."
            };

        private enum OutputType : uint
        {
            Normal = 0,
            Live = 1,
            Error = 2,
            Progress = 3,
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int recordTraceCallback(
            [In] uint type,
            [In] IntPtr data,
            [In] UIntPtr dataLen);

        [LibraryImport("recordtrace")]
        private static partial int RecordTrace(
            byte[] command,
            UIntPtr commandLen,
            recordTraceCallback callback);
    }
}
