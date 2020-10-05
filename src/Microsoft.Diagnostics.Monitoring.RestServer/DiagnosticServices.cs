// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring.Contracts;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    public sealed class DiagnosticServices : IDiagnosticServices
    {
        // String returned for a process field when its value could not be retrieved. This is the same
        // value that is returned by the runtime when it could not determine the value for each of those fields.
        private const string ProcessFieldUnknownValue = "unknown";

        // The value of the operating system field of the ProcessInfo result when the target process is running
        // on a Windows operating system.
        private const string ProcessOperatingSystemWindowsValue = "windows";

        // A Docker container's entrypoint process ID is 1
        private static readonly ProcessFilter DockerEntrypointProcessFilter = new ProcessFilter(1);

        // The amount of time to wait when checking if the docker entrypoint process is a .NET process
        // with a diagnostics transport connection.
        private static readonly TimeSpan DockerEntrypointWaitTimeout = TimeSpan.FromMilliseconds(250);
        // The amount of time to wait before cancelling get additional process information (e.g. getting
        // the process command line if the IEndpointInfo doesn't provide it).
        private static readonly TimeSpan ExtendedProcessInfoTimeout = TimeSpan.FromMilliseconds(500);

        private readonly IEndpointInfoSourceInternal _endpointInfoSource;
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public DiagnosticServices(IEndpointInfoSource endpointInfoSource)
        {
            _endpointInfoSource = (IEndpointInfoSourceInternal)endpointInfoSource;
        }

        public async Task<IEnumerable<IProcessInfo>> GetProcessesAsync(CancellationToken token)
        {
            try
            {
                using CancellationTokenSource extendedInfoCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                IList<Task<ProcessInfo>> processInfoTasks = new List<Task<ProcessInfo>>();
                foreach (IEndpointInfo endpointInfo in await _endpointInfoSource.GetEndpointInfoAsync(token))
                {
                    processInfoTasks.Add(ProcessInfo.FromEndpointInfoAsync(endpointInfo, extendedInfoCancellation.Token));
                }

                // FromEndpointInfoAsync can fill in the command line for .NET Core 3.1 processes by invoking the
                // event pipe and capturing the ProcessInfo event. Timebox this operation with the cancellation token
                // so that getting the process list does not take a long time or wait indefinitely.
                extendedInfoCancellation.CancelAfter(ExtendedProcessInfoTimeout);

                await Task.WhenAll(processInfoTasks);

                return processInfoTasks.Select(t => t.Result);
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Unable to enumerate processes.");
            }
        }

        public async Task<Stream> GetDump(IProcessInfo pi, DumpType mode, CancellationToken token)
        {
            string dumpFilePath = Path.Combine(Path.GetTempPath(), FormattableString.Invariant($"{Guid.NewGuid()}_{pi.ProcessId}"));
            NETCore.Client.DumpType dumpType = MapDumpType(mode);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Get the process
                Process process = Process.GetProcessById(pi.ProcessId);
                await Dumper.CollectDumpAsync(process, dumpFilePath, dumpType);
            }
            else
            {
                await Task.Run(() =>
                {
                    pi.Client.WriteDump(dumpType, dumpFilePath);
                });
            }

            return new AutoDeleteFileStream(dumpFilePath);
        }

        private static NETCore.Client.DumpType MapDumpType(DumpType dumpType)
        {
            switch (dumpType)
            {
                case DumpType.Full:
                    return NETCore.Client.DumpType.Full;
                case DumpType.WithHeap:
                    return NETCore.Client.DumpType.WithHeap;
                case DumpType.Triage:
                    return NETCore.Client.DumpType.Triage;
                case DumpType.Mini:
                    return NETCore.Client.DumpType.Normal;
                default:
                    throw new ArgumentException("Unexpected dumpType", nameof(dumpType));
            }
        }

        public async Task<IProcessInfo> GetProcessAsync(ProcessFilter? filter, CancellationToken token)
        {
            var endpointInfos = await _endpointInfoSource.GetEndpointInfoAsync(token);

            if (filter.HasValue)
            {
                return await GetSingleProcessInfoAsync(
                    endpointInfos,
                    filter);
            }

            // Short-circuit for when running in a Docker container.
            if (RuntimeInfo.IsInDockerContainer)
            {
                try
                {
                    IProcessInfo processInfo = await GetSingleProcessInfoAsync(
                        endpointInfos,
                        DockerEntrypointProcessFilter);

                    using var timeoutSource = new CancellationTokenSource(DockerEntrypointWaitTimeout);

                    await processInfo.Client.WaitForConnectionAsync(timeoutSource.Token);

                    return processInfo;
                }
                catch
                {
                    // Process ID 1 doesn't exist, didn't advertise in connect mode, or is not a .NET process.
                }
            }

            return await GetSingleProcessInfoAsync(
                endpointInfos,
                filter: null);
        }

        private async Task<IProcessInfo> GetSingleProcessInfoAsync(IEnumerable<IEndpointInfo> endpointInfos, ProcessFilter? filter)
        {
            if (filter.HasValue)
            {
                if (filter.Value.RuntimeInstanceCookie.HasValue)
                {
                    Guid cookie = filter.Value.RuntimeInstanceCookie.Value;
                    endpointInfos = endpointInfos.Where(info => info.RuntimeInstanceCookie == cookie);
                }

                if (filter.Value.ProcessId.HasValue)
                {
                    int pid = filter.Value.ProcessId.Value;
                    endpointInfos = endpointInfos.Where(info => info.ProcessId == pid);
                }
            }

            IEndpointInfo[] endpointInfoArray = endpointInfos.ToArray();
            switch (endpointInfoArray.Length)
            {
                case 0:
                    throw new ArgumentException("Unable to discover a target process.");
                case 1:
                    return await ProcessInfo.FromEndpointInfoAsync(endpointInfoArray[0]);
                default:
#if DEBUG
                    IEndpointInfo endpointInfo = endpointInfoArray.FirstOrDefault(info => string.Equals(Process.GetProcessById(info.ProcessId).ProcessName, "iisexpress", StringComparison.OrdinalIgnoreCase));
                    if (endpointInfo != null)
                    {
                        return await ProcessInfo.FromEndpointInfoAsync(endpointInfo);
                    }
#endif
                    throw new ArgumentException("Unable to select a single target process because multiple target processes have been discovered.");
            }
        }

        public void Dispose()
        {
            _tokenSource.Cancel();
        }

        /// <summary>
        /// We want to make sure we destroy files we finish streaming.
        /// We want to make sure that we stream out files since we compress on the fly; the size cannot be known upfront.
        /// CONSIDER The above implies knowledge of how the file is used by the rest api.
        /// </summary>
        private sealed class AutoDeleteFileStream : FileStream
        {
            public AutoDeleteFileStream(string path) : base(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096, FileOptions.DeleteOnClose)
            {
            }

            public override bool CanSeek => false;
        }


        private sealed class ProcessInfo : IProcessInfo
        {
            public ProcessInfo(
                DiagnosticsClient client,
                Guid runtimeInstanceIdentifier,
                int processId,
                string processName,
                string commandLine,
                string operatingSystem,
                string processArchitecture)
            {
                Client = client;
                CommandLine = commandLine;
                ProcessId = processId;
                ProcessName = processName;
                RuntimeInstanceCookie = runtimeInstanceIdentifier;
                OperatingSystem = operatingSystem;
                ProcessArchitecture = processArchitecture;
            }

            public static async Task<ProcessInfo> FromEndpointInfoAsync(IEndpointInfo endpointInfo)
            {
                using CancellationTokenSource extendedInfoCancellation = new CancellationTokenSource(ExtendedProcessInfoTimeout);
                return await FromEndpointInfoAsync(endpointInfo, extendedInfoCancellation.Token);
            }

            // Creates a ProcessInfo object from the IEndpointInfo. Attempts to get the command line using event pipe
            // if the endpoint information doesn't provide it. The cancelation token can be used to timebox this fallback
            // mechansim.
            public static async Task<ProcessInfo> FromEndpointInfoAsync(IEndpointInfo endpointInfo, CancellationToken extendedInfoCancellationToken)
            {
                if (null == endpointInfo)
                {
                    throw new ArgumentNullException(nameof(endpointInfo));
                }

                var client = new DiagnosticsClient(endpointInfo.Endpoint);

                string commandLine = endpointInfo.CommandLine;
                if (string.IsNullOrEmpty(commandLine))
                {
                    await using var processor = new DiagnosticsEventPipeProcessor(
                        PipeMode.ProcessInfo,
                        processInfoCallback: cmdLine => { commandLine = cmdLine; return Task.CompletedTask; });

                    try
                    {
                        await processor.Process(
                            client,
                            endpointInfo.ProcessId,
                            Timeout.InfiniteTimeSpan,
                            extendedInfoCancellationToken);
                    }
                    catch
                    {
                    }
                }

                string processName = null;
                if (!string.IsNullOrEmpty(commandLine))
                {
                    // Get the process name from the command line
                    bool isWindowsProcess = false;
                    if (string.IsNullOrEmpty(endpointInfo.OperatingSystem))
                    {
                        // If operating system is null, the process is likely .NET Core 3.1 (which doesn't have the GetProcessInfo command).
                        // Since the underlying diagnostic communication channel used by the .NET runtime requires that the diagnostic process
                        // must be running on the same type of operating system as the target process (e.g. dotnet-monitor must be running on Windows
                        // if the target process is running on Windows), then checking the local operating system should be a sufficient heuristic
                        // to determine the operating system of the target process.
                        isWindowsProcess = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                    }
                    else
                    {
                        isWindowsProcess = ProcessOperatingSystemWindowsValue.Equals(endpointInfo.OperatingSystem, StringComparison.OrdinalIgnoreCase);
                    }

                    string processPath = CommandLineHelper.ExtractExecutablePath(commandLine, isWindowsProcess);
                    if (!string.IsNullOrEmpty(processPath))
                    {
                        processName = Path.GetFileName(processPath);
                        if (isWindowsProcess)
                        {
                            // Remove the extension on Windows to match the behavior of Process.ProcessName
                            processName = Path.GetFileNameWithoutExtension(processName);
                        }
                    }
                }

                // The GetProcessInfo command will return "unknown" for values for which it does
                // not know the value, such as operating system and process architecture if the
                // process is running on one that is not predefined. Mimic the same behavior here
                // when the extra process information was not provided.
                return new ProcessInfo(
                    client,
                    endpointInfo.RuntimeInstanceCookie,
                    endpointInfo.ProcessId,
                    processName ?? ProcessFieldUnknownValue,
                    commandLine ?? ProcessFieldUnknownValue,
                    endpointInfo.OperatingSystem ?? ProcessFieldUnknownValue,
                    endpointInfo.ProcessArchitecture ?? ProcessFieldUnknownValue);
            }

            public DiagnosticsClient Client { get; }

            public string CommandLine { get; }

            public string OperatingSystem { get; }

            public string ProcessArchitecture { get; }

            public int ProcessId { get; }

            public string ProcessName { get; }

            public Guid RuntimeInstanceCookie { get; }
        }
    }
}
