// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal unsafe struct RuntimeInfo
    {
        public const string RUNTIME_INFO_SYMBOL = "DotNetRuntimeInfo";
        public const string RUNTIME_INFO_SIGNATURE = "DotNetRuntimeInfo";
        public const int RUNTIME_INFO_RUNTIME_VERSION = 2;
        public const int RUNTIME_INFO_LATEST = 2;

        private fixed byte _signature[18];
        private readonly int _version;
        private fixed byte _runtimeModuleIndex[24];
        private fixed byte _dacModuleIndex[24];
        private fixed byte _dbiModuleIndex[24];

        public static bool TryRead(IServiceProvider services, ulong address, out RuntimeInfo info)
        {
            info = default;

            IMemoryService memory = services.GetService<IMemoryService>();
            if (memory is null)
            {
                return false;
            }

            fixed (RuntimeInfo* header = &info)
            {
                if (!memory.ReadMemory(address, new Span<byte>(header, sizeof(RuntimeInfo)), out int bytesRead) || bytesRead != sizeof(RuntimeInfo))
                {
                    info = default;
                    return false;
                }
            }

            return true;
        }

        public bool TryReadVersion(IServiceProvider services, ulong address, out Version runtimeVersion)
        {
            runtimeVersion = null;
            if (_version < RUNTIME_INFO_RUNTIME_VERSION)
            {
                return true;
            }

            IMemoryService memory = services.GetService<IMemoryService>();
            if (memory is null)
            {
                return false;
            }

            // Version 1 ends at the indexes; later versions append optional runtime-version data.
            Span<byte> version = stackalloc byte[4 * sizeof(int)];
            if (!memory.ReadMemory(address + (uint)sizeof(RuntimeInfo), version, out int bytesRead) || bytesRead != version.Length)
            {
                return true;
            }

            int major = BinaryPrimitives.ReadInt32LittleEndian(version);
            int minor = BinaryPrimitives.ReadInt32LittleEndian(version.Slice(sizeof(int)));
            int build = BinaryPrimitives.ReadInt32LittleEndian(version.Slice(2 * sizeof(int)));
            int revision = BinaryPrimitives.ReadInt32LittleEndian(version.Slice(3 * sizeof(int)));
            if (major < 0 || minor < 0 || build < 0 || revision < 0)
            {
                return false;
            }

            runtimeVersion = new Version(major, minor, build, revision);
            return true;
        }

        public string Signature
        {
            get
            {
                fixed (byte* signature = _signature)
                {
                    return Encoding.ASCII.GetString(signature, RUNTIME_INFO_SIGNATURE.Length);
                }
            }
        }

        public int Version => _version;

        public bool IsValid => _version > 0
            && Signature == RUNTIME_INFO_SIGNATURE
            && _runtimeModuleIndex[0] < 24
            && _dacModuleIndex[0] < 24
            && _dbiModuleIndex[0] < 24;

        public byte[] RawSignature
        {
            get
            {
                fixed (byte* signature = _signature)
                {
                    return new ReadOnlySpan<byte>(signature, 18).ToArray();
                }
            }
        }

        public byte[] RawRuntimeModuleIndex
        {
            get
            {
                fixed (byte* index = _runtimeModuleIndex)
                {
                    return new ReadOnlySpan<byte>(index, 24).ToArray();
                }
            }
        }

        public byte[] RawDacModuleIndex
        {
            get
            {
                fixed (byte* index = _dacModuleIndex)
                {
                    return new ReadOnlySpan<byte>(index, 24).ToArray();
                }
            }
        }

        public byte[] RawDbiModuleIndex
        {
            get
            {
                fixed (byte* index = _dbiModuleIndex)
                {
                    return new ReadOnlySpan<byte>(index, 24).ToArray();
                }
            }
        }

        public (int timeStamp, int fileSize) RuntimePEIIndex
        {
            get
            {
                fixed (byte* index = _runtimeModuleIndex)
                {
                    return GetPEIndex(index);
                }
            }
        }

        public (int timeStamp, int fileSize) DacPEIndex
        {
            get
            {
                fixed (byte* index = _dacModuleIndex)
                {
                    return GetPEIndex(index);
                }
            }
        }

        public (int timeStamp, int fileSize) DbiPEIndex
        {
            get
            {
                fixed (byte* index = _dbiModuleIndex)
                {
                    return GetPEIndex(index);
                }
            }
        }

        private static (int timeStamp, int fileSize) GetPEIndex(byte* index)
        {
            if (index[0] < 2 * sizeof(int))
            {
                return (0, 0);
            }
            ReadOnlySpan<byte> properties = new(index + 1, 2 * sizeof(int));
            return (BinaryPrimitives.ReadInt32LittleEndian(properties), BinaryPrimitives.ReadInt32LittleEndian(properties.Slice(sizeof(int))));
        }

        public ImmutableArray<byte> RuntimeBuildId
        {
            get
            {
                fixed (byte* index = _runtimeModuleIndex)
                {
                    return GetBuildId(index);
                }
            }
        }

        public ImmutableArray<byte> DacBuildId
        {
            get
            {
                fixed (byte* index = _dacModuleIndex)
                {
                    return GetBuildId(index);
                }
            }
        }

        public ImmutableArray<byte> DbiBuildId
        {
            get
            {
                fixed (byte* index = _dbiModuleIndex)
                {
                    return GetBuildId(index);
                }
            }
        }

        private static ImmutableArray<byte> GetBuildId(byte* index)
        {
            // Invalid headers are still displayed by diagnostic commands; never read beyond the slot.
            ReadOnlySpan<byte> buildId = new(index + 1, Math.Min((int)index[0], 23));
            return buildId.ToArray().ToImmutableArray();
        }
    }
}
