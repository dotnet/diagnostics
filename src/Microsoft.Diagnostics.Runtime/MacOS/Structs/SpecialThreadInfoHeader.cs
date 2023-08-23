// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace Microsoft.Diagnostics.Runtime.MacOS.Structs
{
    internal unsafe struct SpecialThreadInfoHeader
    {
        public const string SpecialThreadInfoSignature = "THREADINFO";
        public const ulong SpecialThreadInfoAddress = 0x7fffffff00000000;

        private fixed byte _signature[16];
        public string Signature
        {
            get
            {
                fixed (byte* sig = _signature)
                    return Encoding.ASCII.GetString(sig, 16).TrimEnd((char)0);
            }
        }

        public uint ProcessId { get; }
        public uint NumberThreadEntries { get; }
    }

    internal readonly struct SpecialThreadInfoEntry
    {
        public uint ThreadId { get; }
        public ulong StackPointer { get; }
    }
}
