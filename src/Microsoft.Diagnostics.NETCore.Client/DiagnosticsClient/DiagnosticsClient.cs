// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

        internal DiagnosticsClient(string address) :
            this(new AddressIpcEndpoint(address))
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
        public EventPipeSession StartEventPipeSession(IEnumerable<EventPipeProvider> providers, bool requestRundown = true, int circularBufferMB = 256)
        {
            return new EventPipeSession(_endpoint, providers, requestRundown, circularBufferMB);
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
        public EventPipeSession StartEventPipeSession(EventPipeProvider provider, bool requestRundown = true, int circularBufferMB = 256)
        {
            return new EventPipeSession(_endpoint, new[] { provider }, requestRundown, circularBufferMB);
        }

        /// <summary>
        /// Trigger a core dump generation.
        /// </summary> 
        /// <param name="dumpType">Type of the dump to be generated</param>
        /// <param name="dumpPath">Full path to the dump to be generated. By default it is /tmp/coredump.{pid}</param>
        /// <param name="logDumpGeneration">When set to true, display the dump generation debug log to the console.</param>
        public void WriteDump(DumpType dumpType, string dumpPath, bool logDumpGeneration = false)
        {
            if (string.IsNullOrEmpty(dumpPath))
                throw new ArgumentNullException($"{nameof(dumpPath)} required");

            byte[] payload = SerializePayload(dumpPath, (uint)dumpType, logDumpGeneration);
            IpcMessage message = new IpcMessage(DiagnosticsServerCommandSet.Dump, (byte)DumpCommandId.GenerateCoreDump, payload);
            IpcMessage response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    uint hr = BitConverter.ToUInt32(response.Payload, 0);
                    if (hr == (uint)DiagnosticsIpcError.UnknownCommand)
                    {
                        throw new UnsupportedCommandException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                    }
                    throw new ServerErrorException($"Writing dump failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    return;
                default:
                    throw new ServerErrorException($"Writing dump failed - server responded with unknown command");
            }
        }

        /// <summary>
        /// Attach a profiler.
        /// </summary>
        /// <param name="attachTimeout">Timeout for attaching the profiler</param>
        /// <param name="profilerGuid">Guid for the profiler to be attached</param>
        /// <param name="profilerPath">Path to the profiler to be attached</param>
        /// <param name="additionalData">Additional data to be passed to the profiler</param>
        public void AttachProfiler(TimeSpan attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData = null)
        {
            if (profilerGuid == null || profilerGuid == Guid.Empty)
            {
                throw new ArgumentException($"{nameof(profilerGuid)} must be a valid Guid");
            }

            if (String.IsNullOrEmpty(profilerPath))
            {
                throw new ArgumentException($"{nameof(profilerPath)} must be non-null");
            }

            byte[] serializedConfiguration = SerializePayload((uint)attachTimeout.TotalSeconds, profilerGuid, profilerPath, additionalData);
            var message = new IpcMessage(DiagnosticsServerCommandSet.Profiler, (byte)ProfilerCommandId.AttachProfiler, serializedConfiguration);
            var response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    uint hr = BitConverter.ToUInt32(response.Payload, 0);
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

        /// <summary>
        /// Set a profiler as the startup profiler. It is only valid to issue this command
        /// while the runtime is paused in the "reverse server" mode.
        /// </summary>
        /// <param name="profilerGuid">Guid for the profiler to be attached</param>
        /// <param name="profilerPath">Path to the profiler to be attached</param>
        public void SetStartupProfiler(Guid profilerGuid, string profilerPath)
        {
            if (profilerGuid == null || profilerGuid == Guid.Empty)
            {
                throw new ArgumentException($"{nameof(profilerGuid)} must be a valid Guid");
            }

            if (String.IsNullOrEmpty(profilerPath))
            {
                throw new ArgumentException($"{nameof(profilerPath)} must be non-null");
            }

            byte[] serializedConfiguration = SerializePayload(profilerGuid, profilerPath);
            var message = new IpcMessage(DiagnosticsServerCommandSet.Profiler, (byte)ProfilerCommandId.StartupProfiler, serializedConfiguration);
            var response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    uint hr = BitConverter.ToUInt32(response.Payload, 0);
                    if (hr == (uint)DiagnosticsIpcError.UnknownCommand)
                    {
                        throw new UnsupportedCommandException("The target runtime does not support the ProfilerStartup command.");
                    }
                    else if (hr == (uint)DiagnosticsIpcError.InvalidArgument)
                    {
                        throw new ServerErrorException("The runtime must be suspended to issue the SetStartupProfiler command.");
                    }

                    throw new ServerErrorException($"Profiler startup failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    return;
                default:
                    throw new ServerErrorException($"Profiler startup failed - server responded with unknown command");
            }
        }

        /// <summary>
        /// Tell the runtime to resume execution after being paused for "reverse server" mode.
        /// </summary>
        public void ResumeRuntime()
        {
            IpcMessage message = new IpcMessage(DiagnosticsServerCommandSet.Process, (byte)ProcessCommandId.ResumeRuntime);
            var response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    // Try fallback for Preview 7 and Preview 8
                    ResumeRuntimeFallback();
                    return;
                case DiagnosticsServerResponseId.OK:
                    return;
                default:
                    throw new ServerErrorException($"Resume runtime failed - server responded with unknown command");
            }
        }

        /// <summary>
        /// Set an environment variable in the target process.
        /// </summary>
        /// <param name="name">The name of the environment variable to set.</param>
        /// <param name="value">The value of the environment variable to set.</param>
        public void SetEnvironmentVariable(string name, string value)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw new ArgumentException($"{nameof(name)} must be non-null.");
            }

            byte[] serializedConfiguration = SerializePayload(name, value);
            var message = new IpcMessage(DiagnosticsServerCommandSet.Process, (byte)ProcessCommandId.SetEnvironmentVariable, serializedConfiguration);
            var response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    uint hr = BitConverter.ToUInt32(response.Payload, 0);
                    if (hr == (uint)DiagnosticsIpcError.UnknownCommand)
                    {
                        throw new UnsupportedCommandException("The target runtime does not support the SetEnvironmentVariable command.");
                    }

                    throw new ServerErrorException($"SetEnvironmentVariable failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    return;
                default:
                    throw new ServerErrorException($"SetEnvironmentVariable failed - server responded with unknown command");
            }
        }

        /// <summary>
        /// Gets all environement variables and their values from the target process.
        /// </summary>
        /// <returns>A dictionary containing all of the environment variables defined in the target process.</returns>
        public Dictionary<string, string> GetProcessEnvironment()
        {
            var message = new IpcMessage(DiagnosticsServerCommandSet.Process, (byte)ProcessCommandId.GetProcessEnvironment);
            Stream continuation = IpcClient.SendMessage(_endpoint, message, out IpcMessage response);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    int hr = BitConverter.ToInt32(response.Payload, 0);
                    throw new ServerErrorException($"Get process environment failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    ProcessEnvironmentHelper helper = ProcessEnvironmentHelper.Parse(response.Payload);
                    Task<Dictionary<string, string>> envTask = helper.ReadEnvironmentAsync(continuation);
                    envTask.Wait();
                    return envTask.Result;
                default:
                    throw new ServerErrorException($"Get process environment failed - server responded with unknown command");
            }
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


        // Fallback command for .NET 5 Preview 7 and Preview 8
        internal void ResumeRuntimeFallback()
        {
            IpcMessage message = new IpcMessage(DiagnosticsServerCommandSet.Server, (byte)DiagnosticServerCommandId.ResumeRuntime);
            var response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    var hr = BitConverter.ToInt32(response.Payload, 0);
                    throw new ServerErrorException($"Resume runtime failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    return;
                default:
                    throw new ServerErrorException($"Resume runtime failed - server responded with unknown command");
            }
        }

        internal ProcessInfo GetProcessInfo()
        {
            // RE: https://github.com/dotnet/runtime/issues/54083
            // If the GetProcessInfo2 command is sent too early, it will crash the runtime instance.
            // Disable the usage of the command until that issue is fixed.

            // Attempt to get ProcessInfo v2
            //ProcessInfo processInfo = GetProcessInfo2();
            //if (null != processInfo)
            //{
            //    return processInfo;
            //}

            // Attempt to get ProcessInfo v1
            IpcMessage message = new IpcMessage(DiagnosticsServerCommandSet.Process, (byte)ProcessCommandId.GetProcessInfo);
            var response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    var hr = BitConverter.ToInt32(response.Payload, 0);
                    throw new ServerErrorException($"Get process info failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    return ProcessInfo.ParseV1(response.Payload);
                default:
                    throw new ServerErrorException($"Get process info failed - server responded with unknown command");
            }
        }

        private ProcessInfo GetProcessInfo2()
        {
            IpcMessage message = new IpcMessage(DiagnosticsServerCommandSet.Process, (byte)ProcessCommandId.GetProcessInfo2);
            var response = IpcClient.SendMessage(_endpoint, message);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.Error:
                    uint hr = BitConverter.ToUInt32(response.Payload, 0);
                    // In the case that the runtime doesn't understand the GetProcessInfo2 command,
                    // just break to allow fallback to try to get ProcessInfo v1.
                    if (hr == (uint)DiagnosticsIpcError.UnknownCommand)
                    {
                        return null;
                    }
                    throw new ServerErrorException($"GetProcessInfo2 failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerResponseId.OK:
                    return ProcessInfo.ParseV2(response.Payload);
                default:
                    throw new ServerErrorException($"Get process info failed - server responded with unknown command");
            }
        }

        private static byte[] SerializePayload<T>(T arg)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                SerializePayloadArgument(arg, writer);

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] SerializePayload<T1, T2>(T1 arg1, T2 arg2)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                SerializePayloadArgument(arg1, writer);
                SerializePayloadArgument(arg2, writer);

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] SerializePayload<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                SerializePayloadArgument(arg1, writer);
                SerializePayloadArgument(arg2, writer);
                SerializePayloadArgument(arg3, writer);

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] SerializePayload<T1, T2, T3, T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                SerializePayloadArgument(arg1, writer);
                SerializePayloadArgument(arg2, writer);
                SerializePayloadArgument(arg3, writer);
                SerializePayloadArgument(arg4, writer);

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void SerializePayloadArgument<T>(T obj, BinaryWriter writer)
        {
            if (typeof(T) == typeof(string))
            {
                writer.WriteString((string)((object)obj));
            }
            else if (typeof(T) == typeof(int))
            {
                writer.Write((int)((object)obj));
            }
            else if (typeof(T) == typeof(uint))
            {
                writer.Write((uint)((object)obj));
            }
            else if (typeof(T) == typeof(bool))
            {
                bool bValue = (bool)((object)obj);
                uint uiValue = bValue ? (uint)1 : 0;
                writer.Write(uiValue);
            }
            else
            {
                throw new ArgumentException($"Type {obj.GetType()} is not supported in SerializePayloadArgument, please add it.");
            }
        }
    }
}
