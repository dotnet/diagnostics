// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
            Console.WriteLine($"{args.ProcessId}");
            List<string> recordTraceArgs = new();

            recordTraceArgs.Add("--on-cpu");

            return recordTraceArgs;
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
