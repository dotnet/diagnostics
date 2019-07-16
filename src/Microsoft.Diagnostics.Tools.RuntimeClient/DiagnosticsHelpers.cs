// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public static class DiagnosticsHelpers
    {
        /// <summary>
        /// Controls the contents of the dump
        /// </summary>
        public enum DumpType : uint
        {
            Normal = 1,
            WithHeap = 2,
            Triage = 3,
            Full = 4
        }

        /// <summary>
        /// Initiate a core dump in the target process runtime.
        /// </summary>
        /// <param name="processId">.NET Core process id</param>
        /// <param name="dumpName">Path and file name of core dump</param>
        /// <param name="dumpType">Type of dump</param>
        /// <param name="diagnostics">If true, log to console the dump generation diagnostics</param>
        /// <returns>DiagnosticsServerErrorCode</returns>
        public static int GenerateCoreDump(int processId, string dumpName, DumpType dumpType, bool diagnostics)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");

            if (string.IsNullOrEmpty(dumpName))
                throw new ArgumentNullException($"{nameof(dumpName)} required");


            var payload = SerializeCoreDump(dumpName, dumpType, diagnostics);
            var message = new IpcMessage(DiagnosticsServerCommandSet.Dump, (byte)DumpCommandId.GenerateCoreDump, payload);

            var response = IpcClient.SendMessage(processId, message);

            var hr = 0;
            switch ((DiagnosticsServerCommandId)response.Header.CommandId)
            {
                case DiagnosticsServerCommandId.Error:
                case DiagnosticsServerCommandId.OK:
                    hr = BitConverter.ToInt32(response.Payload);
                    break;
                default:
                    return -1;
            }

            return hr;
        }

        /// <summary>
        /// Attach a profiler to the target process runtime.
        /// </summary>
        /// <param name="processId">.NET Core process id</param>
        /// <param name="attachTimeout">The timeout (in ms) for the runtime to wait while attempting to attach.</param>
        /// <param name="profilerGuid">CLSID of the profiler to load</param>
        /// <param name="profilerPath">Path to the profiler library on disk</param>
        /// <param name="additionalData">additional data to pass to the profiler on attach</param>
        /// <returns>HRESULT</returns>
        public static int AttachProfiler(int processId, uint attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData)
        {
            if (profilerGuid == null || profilerGuid == Guid.Empty)
            {
                throw new ArgumentException($"{nameof(profilerGuid)} must be a valid Guid");
            }

            if (String.IsNullOrEmpty(profilerPath))
            {
                throw new ArgumentException($"{nameof(profilerPath)} must be non-null");
            }

            var header = new MessageHeader {
                RequestType = DiagnosticsMessageType.AttachProfiler,
                Pid = (uint)Process.GetCurrentProcess().Id,
            };

            byte[] serializedConfiguration = SerializeProfilerAttach(attachTimeout, profilerGuid, profilerPath, additionalData);
            var message = new IpcMessage(DiagnosticsServerCommandSet.Profiler, (byte)ProfilerCommandId.AttachProfiler, serializedConfiguration);

            var response = IpcClient.SendMessage(processId, message);

            var hr = 0;
            switch ((DiagnosticsServerCommandId)response.Header.CommandId)
            {
                case DiagnosticsServerCommandId.Error:
                case DiagnosticsServerCommandId.OK:
                    hr = BitConverter.ToInt32(response.Payload);
                    break;
                default:
                    hr = -1;
                    break;
            }

            // TODO: the call to set up the pipe and send the message operates on a different timeout than attachTimeout, which is for the runtime.
            // We should eventually have a configurable timeout for the message passing, potentially either separately from the 
            // runtime timeout or respect attachTimeout as one total duration.
            return hr;
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
    }
}
