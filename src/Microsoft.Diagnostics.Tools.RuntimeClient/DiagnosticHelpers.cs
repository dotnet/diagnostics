// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public static class DiagnosticHelpers
    {
        /// <summary>
        /// Controls the contents of the dump
        /// </summary>
        public enum DumpType : int
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
        /// <returns>HRESULT</returns>
        public static int GenerateCoreDump(int processId, string dumpName, DumpType dumpType, bool diagnostics)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                throw new PlatformNotSupportedException($"Unsupported operating system: {RuntimeInformation.OSDescription}");

            if (string.IsNullOrEmpty(dumpName))
                throw new ArgumentNullException($"{nameof(dumpName)} required");

            var header = new MessageHeader {
                RequestType = DiagnosticMessageType.GenerateCoreDump,
                Pid = (uint)Process.GetCurrentProcess().Id,
            };

            byte[] serializedConfiguration;
            using (var stream = new MemoryStream())
                serializedConfiguration = SerializeCoreDump(header, stream, dumpName, dumpType, diagnostics);

            return (int)EventPipeClient.SendCommand(processId, serializedConfiguration);
        }

        /// <summary>
        /// Attach a profiler to the target process runtime.
        /// </summary>
        /// <param name="processId">.NET Core process id</param>
        /// <param name="attachTimeout">The timeout (in ms) to wait while attempting to attach.</param>
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
                RequestType = DiagnosticMessageType.AttachProfiler,
                Pid = (uint)Process.GetCurrentProcess().Id,
            };

            byte[] serializedConfiguration;
            using (var stream = new MemoryStream())
            {
                serializedConfiguration = SerializeProfilerAttach(header, stream, attachTimeout, profilerGuid, profilerPath, additionalData);
            }

            return (int)EventPipeClient.SendCommand(processId, serializedConfiguration);

        }

        private static byte[] SerializeProfilerAttach(MessageHeader header, MemoryStream stream, uint attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData)
        {
            using (var bw = new BinaryWriter(stream))
            {
                bw.Write((uint)header.RequestType);
                bw.Write(header.Pid);

                bw.Write(attachTimeout);
                bw.Write(profilerGuid.ToByteArray());
                bw.WriteString(profilerPath);

                if (additionalData == null)
                {
                    bw.Write(0);
                }
                else
                {
                    bw.Write(additionalData.Length);
                    bw.Write(additionalData);
                }

                bw.Flush();
                stream.Position = 0;

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }
        }

        private static byte[] SerializeCoreDump(MessageHeader header, Stream stream, string dumpName, DumpType dumpType, bool diagnostics)
        {
            using (var bw = new BinaryWriter(stream))
            {
                bw.Write((uint)header.RequestType);
                bw.Write(header.Pid);

                bw.WriteString(dumpName);
                bw.Write((int)dumpType);
                bw.Write(diagnostics ? 1 : 0);

                bw.Flush();
                stream.Position = 0;

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }
        }
    }
}
