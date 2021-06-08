// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// This is a top-level class that contains methods to send various diagnostics command to the runtime.
    /// </summary>
    public sealed class DiagnosticsClient
    {
        private readonly IpcEndpoint _endpoint;

        public DiagnosticsClient(int processId) :
            this(new PidIpcEndpoint(processId))
        {
        }

        internal DiagnosticsClient(IpcEndpoint endpoint)
        {
            _endpoint = endpoint;
        }

        /// <summary>
        /// Wait for an available diagnostic endpoint to the runtime instance.
        /// </summary>
        /// <param name="timeout">The amount of time to wait before cancelling the wait for the connection.</param>
        internal void WaitForConnection(TimeSpan timeout)
        {
            _endpoint.WaitForConnection(timeout);
        }

        /// <summary>
        /// Wait for an available diagnostic endpoint to the runtime instance.
        /// </summary>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task the completes when a diagnostic endpoint to the runtime instance becomes available.
        /// </returns>
        internal Task WaitForConnectionAsync(CancellationToken token)
        {
            return _endpoint.WaitForConnectionAsync(token);
        }

        /// <summary>
        /// Start tracing the application and return an EventPipeSession object
        /// </summary>
        /// <param name="providers">An IEnumerable containing the list of Providers to turn on.</param>
        /// <param name="requestRundown">If true, request rundown events from the runtime</param>
        /// <param name="circularBufferMB">The size of the runtime's buffer for collecting events in MB</param>
        /// <returns>
        /// An EventPipeSession object representing the EventPipe session that just started.
        /// </returns> 
        public EventPipeSession StartEventPipeSession(IEnumerable<EventPipeProvider> providers, bool requestRundown=true, int circularBufferMB=256)
        {
            return EventPipeSession.Start(_endpoint, providers, requestRundown, circularBufferMB);
        }

        /// <summary>
        /// Start tracing the application and return an EventPipeSession object
        /// </summary>
        /// <param name="provider">An EventPipeProvider to turn on.</param>
        /// <param name="requestRundown">If true, request rundown events from the runtime</param>
        /// <param name="circularBufferMB">The size of the runtime's buffer for collecting events in MB</param>
        /// <returns>
        /// An EventPipeSession object representing the EventPipe session that just started.
        /// </returns>
        public EventPipeSession StartEventPipeSession(EventPipeProvider provider, bool requestRundown=true, int circularBufferMB=256)
        {
            return EventPipeSession.Start(_endpoint, new[] { provider }, requestRundown, circularBufferMB);
        }

        /// <summary>
        /// Start tracing the application and return an EventPipeSession object
        /// </summary>
        /// <param name="providers">An IEnumerable containing the list of Providers to turn on.</param>
        /// <param name="requestRundown">If true, request rundown events from the runtime</param>
        /// <param name="circularBufferMB">The size of the runtime's buffer for collecting events in MB</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// An EventPipeSession object representing the EventPipe session that just started.
        /// </returns> 
        internal Task<EventPipeSession> StartEventPipeSessionAsync(IEnumerable<EventPipeProvider> providers, bool requestRundown, int circularBufferMB, CancellationToken token)
        {
            return EventPipeSession.StartAsync(_endpoint, providers, requestRundown, circularBufferMB, token);
        }

        /// <summary>
        /// Start tracing the application and return an EventPipeSession object
        /// </summary>
        /// <param name="provider">An EventPipeProvider to turn on.</param>
        /// <param name="requestRundown">If true, request rundown events from the runtime</param>
        /// <param name="circularBufferMB">The size of the runtime's buffer for collecting events in MB</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// An EventPipeSession object representing the EventPipe session that just started.
        /// </returns>
        internal Task<EventPipeSession> StartEventPipeSessionAsync(EventPipeProvider provider, bool requestRundown, int circularBufferMB, CancellationToken token)
        {
            return EventPipeSession.StartAsync(_endpoint, new[] { provider }, requestRundown, circularBufferMB, token);
        }

        /// <summary>
        /// Trigger a core dump generation.
        /// </summary> 
        /// <param name="dumpType">Type of the dump to be generated</param>
        /// <param name="dumpPath">Full path to the dump to be generated. By default it is /tmp/coredump.{pid}</param>
        /// <param name="logDumpGeneration">When set to true, display the dump generation debug log to the console.</param>
        public void WriteDump(DumpType dumpType, string dumpPath, bool logDumpGeneration=false)
        {
            IpcMessage request = CreateWriteDumpMessage(dumpType, dumpPath, logDumpGeneration);
            using IpcResponse response = IpcClient.SendMessage(_endpoint, request);
            ValidateResponse(response, nameof(WriteDump));
        }

        /// <summary>
        /// Trigger a core dump generation.
        /// </summary> 
        /// <param name="dumpType">Type of the dump to be generated</param>
        /// <param name="dumpPath">Full path to the dump to be generated. By default it is /tmp/coredump.{pid}</param>
        /// <param name="logDumpGeneration">When set to true, display the dump generation debug log to the console.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        internal async Task WriteDumpAsync(DumpType dumpType, string dumpPath, bool logDumpGeneration, CancellationToken token)
        {
            IpcMessage request = CreateWriteDumpMessage(dumpType, dumpPath, logDumpGeneration);
            using IpcResponse response = await IpcClient.SendMessageAsync(_endpoint, request, token).ConfigureAwait(false);
            ValidateResponse(response, nameof(WriteDumpAsync));
        }

        /// <summary>
        /// Attach a profiler.
        /// </summary>
        /// <param name="attachTimeout">Timeout for attaching the profiler</param>
        /// <param name="profilerGuid">Guid for the profiler to be attached</param>
        /// <param name="profilerPath">Path to the profiler to be attached</param>
        /// <param name="additionalData">Additional data to be passed to the profiler</param>
        public void AttachProfiler(TimeSpan attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData=null)
        {
            if (profilerGuid == null || profilerGuid == Guid.Empty)
            {
                throw new ArgumentException($"{nameof(profilerGuid)} must be a valid Guid");
            }

            if (String.IsNullOrEmpty(profilerPath))
            {
                throw new ArgumentException($"{nameof(profilerPath)} must be non-null");
            }

            byte[] serializedConfiguration = SerializeProfilerAttach((uint)attachTimeout.TotalSeconds, profilerGuid, profilerPath, additionalData);
            var message = new IpcMessage(DiagnosticsServerCommandSet.Profiler, (byte)ProfilerCommandId.AttachProfiler, serializedConfiguration);
            using IpcResponse response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Message.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    uint hr = BitConverter.ToUInt32(response.Message.Payload, 0);
                    if (hr == (uint)DiagnosticsIpcError.UnknownCommand)
                    {
                      throw new UnsupportedCommandException("The target runtime does not support profiler attach");
                    }
                    if (hr == (uint)DiagnosticsIpcError.ProfilerAlreadyActive)
                    {
                        throw new ProfilerAlreadyActiveException("The request to attach a profiler was denied because a profiler is already loaded");
                    }
                    throw new ServerErrorException($"Profiler attach failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    return;
                default:
                    throw new ServerErrorException($"Profiler attach failed - server responded with unknown command");
            }

            // The call to set up the pipe and send the message operates on a different timeout than attachTimeout, which is for the runtime.
            // We should eventually have a configurable timeout for the message passing, potentially either separately from the 
            // runtime timeout or respect attachTimeout as one total duration.
        }

        internal void ResumeRuntime()
        {
            IpcMessage request = CreateResumeRuntimeMessage();
            using IpcResponse response = IpcClient.SendMessage(_endpoint, request);
            ValidateResponse(response, nameof(ResumeRuntime));
        }

        internal async Task ResumeRuntimeAsync(CancellationToken cancellationToken)
        {
            IpcMessage request = CreateResumeRuntimeMessage();
            using IpcResponse response = await IpcClient.SendMessageAsync(_endpoint, request, cancellationToken).ConfigureAwait(false);
            ValidateResponse(response, nameof(ResumeRuntimeAsync));
        }

        internal ProcessInfo GetProcessInfo()
        {
            IpcMessage request = CreateProcessInfoMessage();
            using IpcResponse response = IpcClient.SendMessage(_endpoint, request);
            return GetProcessInfoFromResponse(response, nameof(GetProcessInfoAsync));
        }

        internal async Task<ProcessInfo> GetProcessInfoAsync(CancellationToken token)
        {
            IpcMessage request = CreateProcessInfoMessage();
            using IpcResponse response = await IpcClient.SendMessageAsync(_endpoint, request, token).ConfigureAwait(false);
            return GetProcessInfoFromResponse(response, nameof(GetProcessInfoAsync));
        }

        public Dictionary<string,string> GetProcessEnvironment()
        {
            IpcMessage message = CreateProcessEnvironmentMessage();
            using IpcResponse response = IpcClient.SendMessage(_endpoint, message);
            Task<Dictionary<string, string>> envTask = GetProcessEnvironmentFromResponse(response, nameof(GetProcessEnvironment), CancellationToken.None);
            envTask.Wait();
            return envTask.Result;
        }

        internal async Task<Dictionary<string, string>> GetProcessEnvironmentAsync(CancellationToken token)
        {
            IpcMessage message = CreateProcessEnvironmentMessage();
            using IpcResponse response = await IpcClient.SendMessageAsync(_endpoint, message, token).ConfigureAwait(false);
            return await GetProcessEnvironmentFromResponse(response, nameof(GetProcessEnvironmentAsync), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Get all the active processes that can be attached to.
        /// </summary>
        /// <returns>
        /// IEnumerable of all the active process IDs.
        /// </returns>
        public static IEnumerable<int> GetPublishedProcesses()
        {
            static IEnumerable<int> GetAllPublishedProcesses()
            {
                foreach (var port in Directory.GetFiles(PidIpcEndpoint.IpcRootPath))
                {
                    var fileName = new FileInfo(port).Name;
                    var match = Regex.Match(fileName, PidIpcEndpoint.DiagnosticsPortPattern);
                    if (!match.Success) continue;
                    var group = match.Groups[1].Value;
                    if (!int.TryParse(group, NumberStyles.Integer, CultureInfo.InvariantCulture, out var processId))
                        continue;

                    yield return processId;
                }
            }

            return GetAllPublishedProcesses().Distinct();
        }

        private static byte[] SerializeCoreDump(string dumpName, DumpType dumpType, bool diagnostics)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.WriteString(dumpName);
                writer.Write((uint)dumpType);
                writer.Write((uint)(diagnostics ? 1 : 0));

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] SerializeProfilerAttach(uint attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(attachTimeout);
                writer.Write(profilerGuid.ToByteArray());
                writer.WriteString(profilerPath);

                if (additionalData == null)
                {
                    writer.Write(0);
                }
                else
                {
                    writer.Write(additionalData.Length);
                    writer.Write(additionalData);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static IpcMessage CreateProcessEnvironmentMessage()
        {
            return new IpcMessage(DiagnosticsServerCommandSet.Process, (byte)ProcessCommandId.GetProcessEnvironment);
        }

        private static IpcMessage CreateProcessInfoMessage()
        {
            return new IpcMessage(DiagnosticsServerCommandSet.Process, (byte)ProcessCommandId.GetProcessInfo);
        }

        private static IpcMessage CreateResumeRuntimeMessage()
        {
            return new IpcMessage(DiagnosticsServerCommandSet.Process, (byte)ProcessCommandId.ResumeRuntime);
        }

        private static IpcMessage CreateWriteDumpMessage(DumpType dumpType, string dumpPath, bool logDumpGeneration)
        {
            if (string.IsNullOrEmpty(dumpPath))
                throw new ArgumentNullException($"{nameof(dumpPath)} required");

            byte[] payload = SerializeCoreDump(dumpPath, dumpType, logDumpGeneration);
            return new IpcMessage(DiagnosticsServerCommandSet.Dump, (byte)DumpCommandId.GenerateCoreDump, payload);
        }

        private static Task<Dictionary<string, string>> GetProcessEnvironmentFromResponse(IpcResponse response, string operationName, CancellationToken token)
        {
            ValidateResponse(response, operationName);

            ProcessEnvironmentHelper helper = ProcessEnvironmentHelper.Parse(response.Message.Payload);
            return helper.ReadEnvironmentAsync(response.Continuation, token);
        }

        private static ProcessInfo GetProcessInfoFromResponse(IpcResponse response, string operationName)
        {
            ValidateResponse(response, operationName);

            return ProcessInfo.Parse(response.Message.Payload);
        }

        private static void ValidateResponse(IpcResponse response, string operationName)
        {
            switch ((DiagnosticsServerResponseId)response.Message.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    uint hr = BitConverter.ToUInt32(response.Message.Payload, 0);
                    if (hr == (uint)DiagnosticsIpcError.UnknownCommand)
                    {
                        throw new UnsupportedCommandException("{callerName} failed - Command is not supported.");
                    }
                    throw new ServerErrorException($"{operationName} failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    return;
                default:
                    throw new ServerErrorException($"{operationName} failed - server responded with unknown command.");
            }
        }
    }
}
