// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal class ProcessEnvironmentHelper
    {
        private const int CopyBufferSize = (16 << 10) /* 16KiB */;

        private ProcessEnvironmentHelper() { }
        public static ProcessEnvironmentHelper Parse(byte[] payload)
        {
            ProcessEnvironmentHelper helper = new();

            helper.ExpectedSizeInBytes = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(payload, 0, 4));
            helper.Future = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(payload, 4, 2));

            return helper;
        }

        public Dictionary<string, string> ReadEnvironment(Stream continuation)
        {
            using MemoryStream memoryStream = new();
            continuation.CopyTo(memoryStream, CopyBufferSize);
            return ReadEnvironmentCore(memoryStream);
        }

        public async Task<Dictionary<string, string>> ReadEnvironmentAsync(Stream continuation, CancellationToken token = default(CancellationToken))
        {
            using MemoryStream memoryStream = new();
            await continuation.CopyToAsync(memoryStream, CopyBufferSize, token).ConfigureAwait(false);
            return ReadEnvironmentCore(memoryStream);
        }

        private Dictionary<string, string> ReadEnvironmentCore(MemoryStream stream)
        {
            stream.Seek(0, SeekOrigin.Begin);
            byte[] envBlock = stream.ToArray();

            if (envBlock.Length != (long)ExpectedSizeInBytes)
            {
                throw new ApplicationException($"ProcessEnvironment continuation length did not match expected length. Expected: {ExpectedSizeInBytes} bytes, Received: {envBlock.Length} bytes");
            }

            Dictionary<string, string> env = new();
            int cursor = 0;
            cursor += sizeof(uint);
            while (cursor < envBlock.Length)
            {
                string pair = IpcHelpers.ReadString(envBlock, ref cursor);
                int equalsIdx = pair.IndexOf('=');
                env[pair.Substring(0, equalsIdx)] = equalsIdx != pair.Length - 1 ? pair.Substring(equalsIdx + 1) : "";
            }

            return env;
        }


        private uint ExpectedSizeInBytes { get; set; }
        private ushort Future { get; set; }
    }
}
