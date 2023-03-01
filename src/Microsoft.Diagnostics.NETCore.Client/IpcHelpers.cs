﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal static class IpcHelpers
    {
        public static string ReadString(byte[] buffer, ref int index)
        {
            // Length of the string of UTF-16 characters
            int length = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(buffer, index, 4));
            index += sizeof(uint);

            int size = (int)length * sizeof(char);
            // The string contains an ending null character; remove it before returning the value
            string value = Encoding.Unicode.GetString(buffer, index, size).Substring(0, length - 1);
            index += size;
            return value;
        }
    }
}
