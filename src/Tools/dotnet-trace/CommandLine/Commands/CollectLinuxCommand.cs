// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Internal.Common.Utils;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class CollectLinuxCommandHandler
    {
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
                return (int)ReturnCode.PlatformNotSupportedError;
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
            return (int)ReturnCode.Ok;
        }

        private static readonly Option<string> PerfEventsOption =
            new("--perf-events")
            {
                Description = @"Comma-separated list of kernel perf events (e.g. syscalls:sys_enter_execve,sched:sched_switch)."
            };
    }
}
