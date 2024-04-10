// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.FileFormats;

namespace Microsoft.Diagnostics.Runtime
{
    public class RuntimeInfo : TStruct
    {
        public const string RUNTIME_INFO_SYMBOL = "DotNetRuntimeInfo";
        public const string RUNTIME_INFO_SIGNATURE = "DotNetRuntimeInfo";
        public const int RUNTIME_INFO_RUNTIME_VERSION = 2;
        public const int RUNTIME_INFO_LATEST = 2;

        [ArraySize(18)]
        public readonly byte[] RawSignature;
        public readonly int Version;
        [ArraySize(24)]
        public readonly byte[] RawRuntimeModuleIndex;
        [ArraySize(24)]
        public readonly byte[] RawDacModuleIndex;
        [ArraySize(24)]
        public readonly byte[] RawDbiModuleIndex;
        [ArraySize(4)]
        public readonly int[] RawRuntimeVersion;                // major, minor, build, revision - added in version RUNTIME_INFO_RUNTIME_VERSION

        public static unsafe bool TryRead(IServiceProvider services, ulong address, out RuntimeInfo info)
        {
            info = default;

            Reader reader = services.GetService<Reader>();
            if (reader is null)
            {
                return false;
            }

            try
            {
                info = reader.Read<RuntimeInfo>(address);
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException or BadInputFormatException)
            {
                return false;
            }

            return true;
        }

        public string Signature => Encoding.ASCII.GetString(RawSignature.Take(RUNTIME_INFO_SIGNATURE.Length).ToArray());

        public bool IsValid => Version > 0 && Signature == RUNTIME_INFO_SIGNATURE;

        public (int timeStamp, int fileSize) RuntimePEIIndex => GetPEIndex(RawRuntimeModuleIndex);

        public (int timeStamp, int fileSize) DacPEIndex => GetPEIndex(RawDacModuleIndex);

        public (int timeStamp, int fileSize) DbiPEIndex => GetPEIndex(RawDbiModuleIndex);

        private static (int timeStamp, int fileSize) GetPEIndex(byte[] index)
        {
            if (index[0] < 2 * sizeof(int))
            {
                return (0, 0);
            }
            return (BitConverter.ToInt32(index, 1), BitConverter.ToInt32(index, 1 + sizeof(int)));
        }

        public ImmutableArray<byte> RuntimeBuildId => GetBuildId(RawRuntimeModuleIndex);

        public ImmutableArray<byte> DacBuildId => GetBuildId(RawDacModuleIndex);

        public ImmutableArray<byte> DbiBuildId => GetBuildId(RawDbiModuleIndex);

        private static ImmutableArray<byte> GetBuildId(byte[] index) => index.Skip(1).Take(index[0]).ToImmutableArray();

        public Version RuntimeVersion => Version >= RUNTIME_INFO_RUNTIME_VERSION ? new Version(RawRuntimeVersion[0], RawRuntimeVersion[1], RawRuntimeVersion[2], RawRuntimeVersion[3]) : null;
    }
}
