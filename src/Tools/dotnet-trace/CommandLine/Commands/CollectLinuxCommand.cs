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
        private long statusUpdateTimestamp;
        private Version minRuntimeSupportingUserEventsIPCCommand = new(10, 0, 0);

        internal sealed record CollectLinuxArgs(
            CancellationToken Ct,
            string[] Providers,
            string ClrEventLevel,
            string ClrEvents,
            string[] PerfEvents,
            string[] Profiles,
            FileInfo Output,
            TimeSpan Duration,
            string Name,
            int ProcessId,
            bool Probe);

        public CollectLinuxCommandHandler(IConsole console = null)
        {
            Console = console ?? new DefaultConsole();
            rewriter = new LineRewriter(Console);
        }

        internal static bool IsSupported()
        {
            bool isSupportedLinuxPlatform = false;
            if (OperatingSystem.IsLinux())
            {
                isSupportedLinuxPlatform = true;
                try
                {
                    string ostype = File.ReadAllText("/etc/os-release");
                    isSupportedLinuxPlatform = !ostype.Contains("ID=alpine");
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException) {}
            }

            return isSupportedLinuxPlatform;
        }

        /// <summary>
        /// Collects diagnostic traces using perf_events, a Linux OS technology. collect-linux requires admin privileges to capture kernel- and user-mode events, and by default, captures events from all processes.
        /// This Linux-only command includes the same .NET events as dotnet-trace collect, and it uses the kernel’s user_events mechanism to emit .NET events as perf events, enabling unification of user-space .NET events with kernel-space system events.
        /// </summary>
        internal int CollectLinux(CollectLinuxArgs args)
        {
            if (!IsSupported())
            {
                Console.Error.WriteLine("The collect-linux command is not supported on this platform.");
                Console.Error.WriteLine("For requirements, please visit https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace.");
                return (int)ReturnCode.PlatformNotSupportedError;
            }

            Console.WriteLine("==========================================================================================");
            Console.WriteLine("The collect-linux verb is a new preview feature and relies on an updated version of the");
            Console.WriteLine(".nettrace file format. The latest PerfView release supports these trace files but other");
            Console.WriteLine("ways of using the trace file may not work yet. For more details, see the docs at");
            Console.WriteLine("https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace.");
            Console.WriteLine("==========================================================================================");

            int ret = (int)ReturnCode.TracingError;
            string scriptPath = null;
            try
            {
                if (args.Probe)
                {
                    ret = SupportsCollectLinux(args);
                    return ret;
                }

                if (args.ProcessId != 0 || !string.IsNullOrEmpty(args.Name))
                {
                    if (!ProcessSupportsUserEventsIpcCommand(args.ProcessId, args.Name, out int resolvedProcessId, out string resolvedProcessName, out string detectedRuntimeVersion))
                    {
                        Console.Error.WriteLine($"[ERROR] Process '{resolvedProcessName} ({resolvedProcessId})' cannot be traced by collect-linux. Required runtime: {minRuntimeSupportingUserEventsIPCCommand}. Detected runtime: {detectedRuntimeVersion}");
                        return (int)ReturnCode.TracingError;
                    }
                    args = args with { Name = resolvedProcessName, ProcessId = resolvedProcessId };
                }

                args.Ct.Register(() => stopTracing = true);
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
            catch (DiagnosticToolException dte)
            {
                Console.Error.WriteLine($"[ERROR] {dte.Message}");
                ret = (int)dte.ReturnCode;
            }
            catch (DllNotFoundException dnfe)
            {
                Console.Error.WriteLine($"[ERROR] Could not find or load dependencies for collect-linux. For requirements, please visit https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace");
                Console.Error.WriteLine($"[ERROR] {dnfe.Message}");
                ret = (int)ReturnCode.PlatformNotSupportedError;
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
                ProbeOption,
                CommonOptions.ProfileOption,
                CommonOptions.OutputPathOption,
                CommonOptions.DurationOption,
                CommonOptions.NameOption,
                CommonOptions.ProcessIdOption,
            };
            collectLinuxCommand.TreatUnmatchedTokensAsErrors = true; // collect-linux currently does not support child process tracing.
            collectLinuxCommand.Description = "Collects diagnostic traces using perf_events, a Linux OS technology. collect-linux requires admin privileges to capture kernel- and user-mode events, and by default, captures events from all processes. This Linux-only command includes the same .NET events as dotnet-trace collect, and it uses the kernel’s user_events mechanism to emit .NET events as perf events, enabling unification of user-space .NET events with kernel-space system events. Use --probe (optionally with -p|--process-id or -n|--name) to only check which processes can be traced by collect-linux without collecting a trace.";

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
                    Duration: parseResult.GetValue(CommonOptions.DurationOption),
                    Name: parseResult.GetValue(CommonOptions.NameOption) ?? string.Empty,
                    ProcessId: parseResult.GetValue(CommonOptions.ProcessIdOption),
                    Probe: parseResult.GetValue(ProbeOption)));
                return Task.FromResult(rc);
            });

            return collectLinuxCommand;
        }

        internal int SupportsCollectLinux(CollectLinuxArgs args)
        {
            int ret;
            try
            {
                ProbeOutputMode mode = DetermineProbeOutputMode(args.Output.Name);
                bool generateCsv = mode == ProbeOutputMode.CsvToConsole || mode == ProbeOutputMode.Csv;
                StringBuilder supportedCsv = generateCsv ? new StringBuilder() : null;
                StringBuilder unsupportedCsv = generateCsv ? new StringBuilder() : null;

                if (args.ProcessId != 0 || !string.IsNullOrEmpty(args.Name))
                {
                    bool supports = ProcessSupportsUserEventsIpcCommand(args.ProcessId, args.Name, out int resolvedPid, out string resolvedName, out string detectedRuntimeVersion);
                    BuildProcessSupportCsv(resolvedPid, resolvedName, supports, supportedCsv, unsupportedCsv);

                    if (mode == ProbeOutputMode.Console)
                    {
                        Console.WriteLine($".NET process '{resolvedName} ({resolvedPid})' {(supports ? "supports" : "does NOT support")} the EventPipe UserEvents IPC command used by collect-linux.");
                        if (!supports)
                        {
                            Console.WriteLine($"Required runtime: '{minRuntimeSupportingUserEventsIPCCommand}'. Detected runtime: '{detectedRuntimeVersion}'.");
                        }
                    }
                }
                else
                {
                    if (mode == ProbeOutputMode.Console)
                    {
                        Console.WriteLine($"Probing .NET processes for support of the EventPipe UserEvents IPC command used by collect-linux. Requires runtime '{minRuntimeSupportingUserEventsIPCCommand}' or later.");
                    }
                    StringBuilder supportedProcesses = new();
                    StringBuilder unsupportedProcesses = new();

                    IEnumerable<int> pids = DiagnosticsClient.GetPublishedProcesses();
                    foreach (int pid in pids)
                    {
                        if (pid == Environment.ProcessId)
                        {
                            continue;
                        }

                        bool supports = ProcessSupportsUserEventsIpcCommand(pid, string.Empty, out int resolvedPid, out string resolvedName, out string detectedRuntimeVersion);
                        BuildProcessSupportCsv(resolvedPid, resolvedName, supports, supportedCsv, unsupportedCsv);
                        if (supports)
                        {
                            supportedProcesses.AppendLine($"{resolvedPid} {resolvedName}");
                        }
                        else
                        {
                            unsupportedProcesses.AppendLine($"{resolvedPid} {resolvedName} - Detected runtime: '{detectedRuntimeVersion}'");
                        }
                    }

                    if (mode == ProbeOutputMode.Console)
                    {
                        Console.WriteLine($".NET processes that support the command:");
                        Console.WriteLine(supportedProcesses.ToString());
                        Console.WriteLine($".NET processes that do NOT support the command:");
                        Console.WriteLine(unsupportedProcesses.ToString());
                    }
                }

                if (mode == ProbeOutputMode.CsvToConsole)
                {
                    Console.WriteLine("pid,processName,supportsCollectLinux");
                    Console.Write(supportedCsv?.ToString());
                    Console.Write(unsupportedCsv?.ToString());
                }

                if (mode == ProbeOutputMode.Csv)
                {
                    using StreamWriter writer = new(args.Output.FullName, append: false, Encoding.UTF8);
                    writer.WriteLine("pid,processName,supportsCollectLinux");
                    writer.Write(supportedCsv?.ToString());
                    writer.Write(unsupportedCsv?.ToString());
                    Console.WriteLine($"Successfully wrote EventPipe UserEvents IPC command support results to '{args.Output.FullName}'.");
                }

                ret = (int)ReturnCode.Ok;
            }
            catch (DiagnosticToolException dte)
            {
                Console.WriteLine($"[ERROR] {dte.Message}");
                ret = (int)ReturnCode.ArgumentError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                ret = (int)ReturnCode.UnknownError;
            }

            return ret;
        }

        private static ProbeOutputMode DetermineProbeOutputMode(string outputName)
        {
            if (string.Equals(outputName, CommonOptions.DefaultTraceName, StringComparison.OrdinalIgnoreCase))
            {
                return ProbeOutputMode.Console;
            }
            if (string.Equals(outputName, "stdout", StringComparison.OrdinalIgnoreCase))
            {
                return ProbeOutputMode.CsvToConsole;
            }
            return ProbeOutputMode.Csv;
        }

        private bool ProcessSupportsUserEventsIpcCommand(int pid, string processName, out int resolvedPid, out string resolvedName, out string detectedRuntimeVersion)
        {
            CommandUtils.ResolveProcess(pid, processName, out resolvedPid, out resolvedName);

            bool supports = false;
            DiagnosticsClient client = new(resolvedPid);
            ProcessInfo processInfo = client.GetProcessInfo();
            detectedRuntimeVersion = processInfo.ClrProductVersionString;
            if (processInfo.TryGetProcessClrVersion(out Version version, out bool isPrerelease) &&
                (version > minRuntimeSupportingUserEventsIPCCommand ||
                (version == minRuntimeSupportingUserEventsIPCCommand && !isPrerelease)))
            {
                supports = true;
            }

            return supports;
        }

        private static void BuildProcessSupportCsv(int resolvedPid, string resolvedName, bool supports, StringBuilder supportedCsv, StringBuilder unsupportedCsv)
        {
            if (supportedCsv == null && unsupportedCsv == null)
            {
                return;
            }

            string escapedName = (resolvedName ?? string.Empty).Replace(",", string.Empty);
            if (supports)
            {
                supportedCsv?.AppendLine($"{resolvedPid},{escapedName},true");
            }
            else
            {
                unsupportedCsv?.AppendLine($"{resolvedPid},{escapedName},false");
            }
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
                    throw new DiagnosticToolException($"Invalid perf event specification '{perfEvent}'. Expected format 'provider:event'.");
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

            int pid = args.ProcessId;
            if (pid > 0)
            {
                recordTraceArgs.Add($"--pid");
                recordTraceArgs.Add($"{pid}");
            }

            FileInfo resolvedOutput = ResolveOutputPath(args.Output, args.Name);
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

        private static FileInfo ResolveOutputPath(FileInfo output, string processName)
        {
            if (!string.Equals(output.Name, CommonOptions.DefaultTraceName, StringComparison.OrdinalIgnoreCase))
            {
                return output;
            }

            string traceName = "trace";
            if (!string.IsNullOrEmpty(processName))
            {
                traceName = processName;
            }

            DateTime now = DateTime.Now;
            return new FileInfo($"{traceName}_{now:yyyyMMdd}_{now:HHmmss}.nettrace");
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

            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Enter)
            {
                stopTracing = true;
            }

            if (ot == OutputType.Progress)
            {
                long currentTimestamp = Stopwatch.GetTimestamp();
                if (statusUpdateTimestamp != 0 && currentTimestamp < statusUpdateTimestamp)
                {
                    return stopTracing ? 1 : 0;
                }

                if (statusUpdateTimestamp == 0)
                {
                    rewriter.LineToClear = Console.CursorTop - 1;
                }
                else
                {
                    rewriter.RewriteConsoleLine();
                }

                statusUpdateTimestamp = currentTimestamp + Stopwatch.Frequency;
                Console.Out.WriteLine($"[{stopwatch.Elapsed:dd\\:hh\\:mm\\:ss}]\tRecording trace.");
                Console.Out.WriteLine("Press <Enter> or <Ctrl-C> to exit...");
            }

            return stopTracing ? 1 : 0;
        }

        private static readonly Option<string> PerfEventsOption =
            new("--perf-events")
            {
                Description = @"Comma-separated list of perf events (e.g. syscalls:sys_enter_execve,sched:sched_switch)."
            };

        private static readonly Option<bool> ProbeOption =
            new("--probe")
            {
                Description = "Probe .NET processes for support of the EventPipe UserEvents IPC command used by collect-linux, without collecting a trace. Results list supported processes first. Use '-o stdout' to print CSV (pid,processName,supportsCollectLinux) to the console, or '-o <file>' to write the CSV. Probe a single process with -n|--name or -p|--process-id.",
            };

        private enum ProbeOutputMode
        {
            Console,
            Csv,
            CsvToConsole,
        }

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
