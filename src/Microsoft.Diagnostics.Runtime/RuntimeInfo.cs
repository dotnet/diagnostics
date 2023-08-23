// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.Runtime
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal unsafe struct ClrRuntimeInfo
    {
        public const string SymbolValue = "DotNetRuntimeInfo";
        public const int SignatureValueLength = 18;

        private fixed byte _signature[18];
        private readonly int _version;
        private fixed byte _runtimeModuleIndex[24];
        private fixed byte _dacModuleIndex[24];
        private fixed byte _dbiModuleIndex[24];

        public static unsafe bool TryReadClrRuntimeInfo(IDataReader reader, ulong address, out ClrRuntimeInfo header, out Version version)
        {
            header = default;
            version = new Version();

            int size = sizeof(ClrRuntimeInfo);
            fixed (ClrRuntimeInfo* pHeader = &header)
                if (reader.Read(address, new Span<byte>(pHeader, size)) != size || !header.IsValid)
                    return false;

            if (header._version >= 2)
            {
                Span<int> runtimeVersion = stackalloc int[4];
                Span<byte> buffer = MemoryMarshal.Cast<int, byte>(runtimeVersion);

                if (reader.Read(address + (uint)size, buffer) == buffer.Length)
                    version = new Version(runtimeVersion[0], runtimeVersion[1], runtimeVersion[2], runtimeVersion[3]);
            }

            return true;
        }

        public bool IsValid
        {
            get
            {
                fixed (byte* ptr = _signature)
                    return _version > 0 && Encoding.ASCII.GetString(ptr, SymbolValue.Length) == SymbolValue;
            }
        }

        public (int TimeStamp, int FileSize) RuntimePEProperties
        {
            get
            {
                fixed (byte* ptr = _runtimeModuleIndex)
                    return GetProperties(ptr);
            }
        }

        public (int TimeStamp, int FileSize) DacPEProperties
        {
            get
            {
                fixed (byte* ptr = _dacModuleIndex)
                    return GetProperties(ptr);
            }
        }

        public (int TimeStamp, int FileSize) DbiPEProperties
        {
            get
            {
                fixed (byte* ptr = _dbiModuleIndex)
                    return GetProperties(ptr);
            }
        }
        private (int TimeStamp, int FileSize) GetProperties(byte* ptr)
        {
            if (ptr[0] < 2 * sizeof(int))
                return (0, 0);

            return (Unsafe.As<byte, int>(ref ptr[1]), Unsafe.As<byte, int>(ref ptr[1 + sizeof(int)]));
        }

        public ImmutableArray<byte> RuntimeBuildId
        {
            get
            {
                fixed (byte* ptr = _runtimeModuleIndex)
                    return GetBuildId(ptr);
            }
        }


        public ImmutableArray<byte> DacBuildId
        {
            get
            {
                fixed (byte* ptr = _dacModuleIndex)
                    return GetBuildId(ptr);
            }
        }


        public ImmutableArray<byte> DbiBuildId
        {
            get
            {
                fixed (byte* ptr = _dbiModuleIndex)
                    return GetBuildId(ptr);
            }
        }


        private ImmutableArray<byte> GetBuildId(byte* ptr)
        {
            Span<byte> buffer = new(ptr + 1, ptr[0]);
            return buffer.ToArray().ToImmutableArray();
        }
    }
}
