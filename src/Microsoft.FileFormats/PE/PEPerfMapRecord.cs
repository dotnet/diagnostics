// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;

namespace Microsoft.FileFormats.PE
{
    public sealed class PEPerfMapRecord
    {
        public string Path { get; private set; }
        public byte[] Signature { get; private set; }
        public uint Version { get; private set; }

        public PEPerfMapRecord(string path, byte[] sig, uint version)
        {
            Path = path;
            Signature = sig;
            Version = version;
        }
    }
}
