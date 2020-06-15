// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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

        public byte[] Magic = Magic_V1;
        public UInt64 ProcessId;
        public Guid RuntimeInstanceCookie;

        private UInt16 Future;

        public static IpcAdvertise Parse(Stream stream)
        {
            var reader = new BinaryReader(stream);
            var advertise = new IpcAdvertise()
            {
                Magic = reader.ReadBytes(Magic_V1.Length),
                RuntimeInstanceCookie = new Guid(reader.ReadBytes(16)),
                ProcessId = reader.ReadUInt64(),
                Future = reader.ReadUInt16()
            };

            for (int i = 0; i < Magic_V1.Length; i++)
                if (advertise.Magic[i] != Magic_V1[i])
                    throw new Exception("Invalid advertise message from client connection");

            // FUTURE: switch on incoming magic and change if version ever increments
            return advertise;
        }

        public override string ToString()
        {
            return $"{{ Magic={Magic}; ClrInstanceId={RuntimeInstanceCookie}; ProcessId={ProcessId}; Future={Future} }}";
        }
    }
}
