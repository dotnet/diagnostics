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
                serializedConfiguration = Serialize(header, stream, dumpName, dumpType, diagnostics);

            return (int)EventPipeClient.SendCommand(processId, serializedConfiguration);
        }

        private static byte[] Serialize(MessageHeader header, Stream stream, string dumpName, DumpType dumpType, bool diagnostics)
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
