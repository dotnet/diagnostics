// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /**
     * ==ADVERTISE PROTOCOL==
     * Before standard IPC Protocol communication can occur on a client-mode connection
     * the runtime must advertise itself over the connection. ALL SUBSEQUENT COMMUNICATION 
     * IS STANDARD DIAGNOSTICS IPC PROTOCOL COMMUNICATION.
     * 
     * The flow for Advertise is a one-way burst of 24 bytes consisting of
     * 8 bytes  - "ADVR_V1\0" (ASCII chars + null byte)
     * 16 bytes - CLR Instance Cookie (little-endian)
     * 8 bytes  - PID (little-endian)
     * 2 bytes  - future
     */

    internal sealed class IpcAdvertise
    {
        private static byte[] Magic_V1 => Encoding.ASCII.GetBytes("ADVR_V1" + '\0');

        private IpcAdvertise(byte[] magic, Guid cookie, UInt64 pid, UInt16 future)
        {
            Future = future;
            Magic = magic;
            ProcessId = pid;
            RuntimeInstanceCookie = cookie;
        }

        public static IpcAdvertise Parse(Stream stream)
        {
            var reader = new BinaryReader(stream);
            byte[] magic = reader.ReadBytes(Magic_V1.Length);
            Guid cookie = new Guid(reader.ReadBytes(16));
            UInt64 pid = reader.ReadUInt64();
            UInt16 future = reader.ReadUInt16();

            if (!Magic_V1.SequenceEqual(magic))
            {
                throw new Exception("Invalid advertise message from client connection");
            }

            // FUTURE: switch on incoming magic and change if version ever increments
            return new IpcAdvertise(magic, cookie, pid, future);
        }

        public override string ToString()
        {
            return $"{{ Magic={Magic}; ClrInstanceId={RuntimeInstanceCookie}; ProcessId={ProcessId}; Future={Future} }}";
        }

        private UInt16 Future { get; } = 0;
        public byte[] Magic { get; } = Magic_V1;
        public UInt64 ProcessId { get; } = 0;
        public Guid RuntimeInstanceCookie { get; } = Guid.Empty;
    }
}
