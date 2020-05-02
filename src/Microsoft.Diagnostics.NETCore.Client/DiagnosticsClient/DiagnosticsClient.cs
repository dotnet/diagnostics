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

namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// This is a top-level class that contains methods to send various diagnostics command to the runtime.
    /// </summary>
    public sealed class DiagnosticsClient
    {
        private readonly int _processId;

        public DiagnosticsClient(int processId)
        {
            _processId = processId;
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
            return new EventPipeSession(_processId, providers, requestRundown, circularBufferMB);
        }

        /// <summary>
        /// Trigger a core dump generation.
        /// </summary> 
        /// <param name="dumpType">Type of the dump to be generated</param>
        /// <param name="dumpPath">Full path to the dump to be generated. By default it is /tmp/coredump.{pid}</param>
        /// <param name="logDumpGeneration">When set to true, display the dump generation debug log to the console.</param>
        public void WriteDump(DumpType dumpType, string dumpPath, bool logDumpGeneration=false)
        {
            if (!(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows)))
                throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");

            if (string.IsNullOrEmpty(dumpPath))
                throw new ArgumentNullException($"{nameof(dumpPath)} required");

            byte[] payload = SerializeCoreDump(dumpPath, dumpType, logDumpGeneration);
            IpcMessage message = new IpcMessage(DiagnosticsServerCommandSet.Dump, (byte)DumpCommandId.GenerateCoreDump, payload);
            IpcMessage response = IpcClient.SendMessage(_processId, message);
            switch ((DiagnosticsServerCommandId)response.Header.CommandId)
            {
                case DiagnosticsServerCommandId.Error:
                    uint hr = BitConverter.ToUInt32(response.Payload, 0);
                    if (hr == (uint)DiagnosticsIpcError.UnknownCommand) {
                        throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");
                    }
                    throw new ServerErrorException($"Writing dump failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerCommandId.OK:
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
            var response = IpcClient.SendMessage(_processId, message);
            switch ((DiagnosticsServerCommandId)response.Header.CommandId)
            {
                case DiagnosticsServerCommandId.Error:
                    var hr = BitConverter.ToInt32(response.Payload, 0);
                    throw new ServerErrorException($"Profiler attach failed (HRESULT: 0x{hr:X8})");
                case DiagnosticsServerCommandId.OK:
                    return;
                default:
                    throw new ServerErrorException($"Profiler attach failed - server responded with unknown command");
            }

            // The call to set up the pipe and send the message operates on a different timeout than attachTimeout, which is for the runtime.
            // We should eventually have a configurable timeout for the message passing, potentially either separately from the 
            // runtime timeout or respect attachTimeout as one total duration.
        }

        /// <summary>
        /// Get all the active processes that can be attached to.
        /// </summary>
        /// <returns>
        /// IEnumerable of all the active process IDs.
        /// </returns>
        public static IEnumerable<int> GetPublishedProcesses()
        {
            return Directory.GetFiles(IpcClient.IpcRootPath)
                .Select(namedPipe => (new FileInfo(namedPipe)).Name)
                .Where(input => Regex.IsMatch(input, IpcClient.DiagnosticsPortPattern))
                .Select(input => int.Parse(Regex.Match(input, IpcClient.DiagnosticsPortPattern).Groups[1].Value, NumberStyles.Integer))
                .Distinct();
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
    }
}
