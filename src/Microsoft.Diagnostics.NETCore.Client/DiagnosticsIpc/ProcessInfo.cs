// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client.DiagnosticsIpc;
using System;
using System.Text;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /**
     * ==ProcessInfo==
     * The response payload to issuing the GetProcessInfo command.
     * 
     * 8 bytes  - PID (little-endian)
     * 16 bytes - CLR Runtime Instance Cookie (little-endian)
     * # bytes  - Command line string length and data
     * # bytes  - Operating system string length and data
     * # bytes  - Process architecture string length and data
     * 
     * The "string length and data" fields are variable length:
     * 4 bytes            - Length of string data in UTF-16 characters
     * (2 * length) bytes - The data of the string encoded using Unicode
     *                      (includes null terminating character)
     */

    internal class ProcessInfo
    {
        private static readonly int GuidSizeInBytes = 16;

        public static ProcessInfo Parse(byte[] payload)
        {
            ProcessInfo processInfo = new ProcessInfo();

            int index = 0;
            processInfo.ProcessId = BitConverter.ToUInt64(payload, index);
            index += sizeof(UInt64);

            byte[] cookieBuffer = new byte[GuidSizeInBytes];
            Array.Copy(payload, index, cookieBuffer, 0, GuidSizeInBytes);
            processInfo.RuntimeInstanceCookie = new Guid(cookieBuffer);
            index += GuidSizeInBytes;

            processInfo.CommandLine = IpcHelpers.ReadString(payload, ref index);
            processInfo.OperatingSystem = IpcHelpers.ReadString(payload, ref index);
            processInfo.ProcessArchitecture = IpcHelpers.ReadString(payload, ref index);

            return processInfo;
        }

        public UInt64 ProcessId { get; private set; }
        public Guid RuntimeInstanceCookie { get; private set; }
        public string CommandLine { get; private set; }
        public string OperatingSystem { get; private set; }
        public string ProcessArchitecture { get; private set; }
    }
}