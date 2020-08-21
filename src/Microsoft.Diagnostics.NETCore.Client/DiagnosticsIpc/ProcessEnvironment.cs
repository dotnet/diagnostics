// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal class ProcessEnvironmentHelper
    {
        private ProcessEnvironmentHelper() {}
        public static ProcessEnvironmentHelper Parse(byte[] payload)
        {
            ProcessEnvironmentHelper helper = new ProcessEnvironmentHelper();

            helper.ExpectedSizeInBytes = BitConverter.ToUInt32(payload, 0);
            helper.Future = BitConverter.ToUInt16(payload, 4);

            return helper;
        }

        public async Task<Dictionary<string,string>> ReadEnvironmentAsync(Stream continuation, CancellationToken token = default(CancellationToken))
        {
            var env = new Dictionary<string,string>();

            using var memoryStream = new MemoryStream();
            await continuation.CopyToAsync(memoryStream, (16 << 10) /* 16KiB */, token);
            memoryStream.Seek(0, SeekOrigin.Begin);
            byte[] envBlock = memoryStream.ToArray();

            if (envBlock.Length != (long)ExpectedSizeInBytes)
                throw new ApplicationException($"ProcessEnvironment continuation length did not match expected length. Expected: {ExpectedSizeInBytes} bytes, Received: {envBlock.Length} bytes");

            int cursor = 0;
            UInt32 nElements = BitConverter.ToUInt32(envBlock, cursor);
            cursor += sizeof(UInt32);
            while (cursor < envBlock.Length)
            {
                string pair = ReadString(envBlock, ref cursor);
                int equalsIdx = pair.IndexOf('=');
                env[pair.Substring(0,equalsIdx)] = equalsIdx != pair.Length - 1 ? pair.Substring(equalsIdx+1) : "";
            }

            return env;
        }

        private static string ReadString(byte[] buffer, ref int index)
        {
            // Length of the string of UTF-16 characters
            int length = (int)BitConverter.ToUInt32(buffer, index);
            index += sizeof(UInt32);

            int size = (int)length * sizeof(char);
            // The string contains an ending null character; remove it before returning the value
            string value = Encoding.Unicode.GetString(buffer, index, size).Substring(0, length - 1);
            index += size;
            return value;
        }

        private UInt32 ExpectedSizeInBytes { get; set; }
        private UInt16 Future { get; set; }
    }
}