﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /**
     * ==ADVERTISE PROTOCOL==
     * Before standard IPC Protocol communication can occur on a client-mode connection
     * the runtime must advertise itself over the connection. ALL SUBSEQUENT COMMUNICATION
     * IS STANDARD DIAGNOSTICS IPC PROTOCOL COMMUNICATION.
     *
     * The flow for Advertise is a one-way burst of 34 bytes consisting of
     * 8 bytes  - "ADVR_V1\0" (ASCII chars + null byte)
     * 16 bytes - CLR Instance Cookie (little-endian)
     * 8 bytes  - PID (little-endian)
     * 2 bytes  - future
     */

    internal sealed class IpcAdvertise
    {
        private static byte[] Magic_V1 => Encoding.ASCII.GetBytes("ADVR_V1" + '\0');
        private static readonly int IpcAdvertiseV1SizeInBytes = Magic_V1.Length + 16 + 8 + 2; // 34 bytes

        private IpcAdvertise(byte[] magic, Guid cookie, ulong pid, ushort future)
        {
            Future = future;
            Magic = magic;
            ProcessId = pid;
            RuntimeInstanceCookie = cookie;
        }

        public static int V1SizeInBytes { get; } = IpcAdvertiseV1SizeInBytes;

        public static async Task<IpcAdvertise> ParseAsync(Stream stream, CancellationToken token)
        {
            byte[] buffer = new byte[IpcAdvertiseV1SizeInBytes];

            int totalRead = 0;
            do
            {
                int read = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, token).ConfigureAwait(false);
                if (0 == read)
                {
                    throw new EndOfStreamException();
                }
                totalRead += read;
            }
            while (totalRead < buffer.Length);

            int index = 0;
            byte[] magic = new byte[Magic_V1.Length];
            Array.Copy(buffer, magic, Magic_V1.Length);
            index += Magic_V1.Length;

            if (!Magic_V1.SequenceEqual(magic))
            {
                throw new Exception("Invalid advertise message from client connection");
            }

            byte[] cookieBuffer = new byte[16];
            Array.Copy(buffer, index, cookieBuffer, 0, 16);
            Guid cookie = new(cookieBuffer);
            index += 16;

            ulong pid = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(buffer, index, 8));
            index += 8;

            ushort future = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(buffer, index, 2));

            // FUTURE: switch on incoming magic and change if version ever increments
            return new IpcAdvertise(magic, cookie, pid, future);
        }

        public static async Task SerializeAsync(Stream stream, Guid runtimeInstanceCookie, ulong processId, CancellationToken token)
        {
            int index = 0;
            byte[] buffer = new byte[IpcAdvertiseV1SizeInBytes];

            Array.Copy(Magic_V1, buffer, Magic_V1.Length);
            index += Magic_V1.Length;

            byte[] cookieBuffer = runtimeInstanceCookie.ToByteArray();
            Array.Copy(cookieBuffer, 0, buffer, index, cookieBuffer.Length);
            index += cookieBuffer.Length;

            byte[] processIdBuffer = BitConverter.GetBytes(processId);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(processIdBuffer);
            }
            Array.Copy(processIdBuffer, 0, buffer, index, sizeof(ulong));
            index += sizeof(ulong);

            short future = 0;
            Array.Copy(BitConverter.GetBytes(future), 0, buffer, index, sizeof(short));
            index += sizeof(short);

            await stream.WriteAsync(buffer, 0, index, token).ConfigureAwait(false);
        }

        public override string ToString()
        {
            return $"{{ Magic={Magic}; ClrInstanceId={RuntimeInstanceCookie}; ProcessId={ProcessId}; Future={Future} }}";
        }

        private ushort Future { get; }
        public byte[] Magic { get; } = Magic_V1;
        public ulong ProcessId { get; }
        public Guid RuntimeInstanceCookie { get; } = Guid.Empty;
    }
}
