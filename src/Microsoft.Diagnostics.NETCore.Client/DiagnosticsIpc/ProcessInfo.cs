// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;

namespace Microsoft.Diagnostics.NETCore.Client
{
    /**
     * ==ProcessInfo==
     * The response payload to issuing the GetProcessInfo command.
     *
     * 8 bytes  - PID (little-endian)
     * 16 bytes - CLR Runtime Instance Cookie (little-endian)
     * # bytes  - Command line string length and data
     * # bytes  - Operating system string length and data
     * # bytes  - Process architecture string length and data
     *
     * ==ProcessInfo2==
     * The response payload to issuing the GetProcessInfo2 command.
     *
     * 8 bytes  - PID (little-endian)
     * 16 bytes - CLR Runtime Instance Cookie (little-endian)
     * # bytes  - Command line string length and data
     * # bytes  - Operating system string length and data
     * # bytes  - Process architecture string length and data
     * # bytes  - Managed entrypoint assembly name
     * # bytes  - CLR product version string (may include prerelease labels)
     *
     *
     * The "string length and data" fields are variable length:
     * 4 bytes            - Length of string data in UTF-16 characters
     * (2 * length) bytes - The data of the string encoded using Unicode
     *                      (includes null terminating character)
     */

    internal class ProcessInfo
    {
        private const int GuidSizeInBytes = 16;

        /// <summary>
        /// Parses a ProcessInfo payload.
        /// </summary>
        internal static ProcessInfo ParseV1(byte[] payload)
        {
            int index = 0;
            return ParseCommon(payload, ref index);
        }

        /// <summary>
        /// Parses a ProcessInfo2 payload.
        /// </summary>
        internal static ProcessInfo ParseV2(byte[] payload)
        {
            int index = 0;
            return ParseCommon2(payload, ref index);
        }

        /// <summary>
        /// Parses a ProcessInfo3 payload.
        /// </summary>
        internal static ProcessInfo ParseV3(byte[] payload)
        {
            int index = 0;

            // The ProcessInfo3 command is intended to allow the addition of new fields in future versions so
            // long as the version field is incremented; prior fields shall not be changed or removed.
            // Read the version field, parse the common payload, and dynamically parse the remainder depending on the version.
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(payload, index, 4));
            index += sizeof(uint);

            ProcessInfo processInfo = ParseCommon2(payload, ref index);

            if (version >= 1)
            {
                processInfo.PortableRuntimeIdentifier = IpcHelpers.ReadString(payload, ref index);
            }

            return processInfo;
        }

        private static ProcessInfo ParseCommon(byte[] payload, ref int index)
        {
            ProcessInfo processInfo = new();

            processInfo.ProcessId = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(payload, index, 8));
            index += sizeof(ulong);

            byte[] cookieBuffer = new byte[GuidSizeInBytes];
            Array.Copy(payload, index, cookieBuffer, 0, GuidSizeInBytes);
            processInfo.RuntimeInstanceCookie = new Guid(cookieBuffer);
            index += GuidSizeInBytes;

            processInfo.CommandLine = IpcHelpers.ReadString(payload, ref index);
            processInfo.OperatingSystem = IpcHelpers.ReadString(payload, ref index);
            processInfo.ProcessArchitecture = IpcHelpers.ReadString(payload, ref index);

            return processInfo;
        }

        internal bool TryGetProcessClrVersion(out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(ClrProductVersionString))
            {
                return false;
            }

            // The version is of the SemVer2 form: <major>.<minor>.<patch>[-<prerelease>][+<metadata>]
            // Remove the prerelease and metadata version information before parsing.

            ReadOnlySpan<char> versionSpan = ClrProductVersionString.AsSpan();
            int metadataIndex = versionSpan.IndexOf('+');
            if (-1 == metadataIndex)
            {
                metadataIndex = versionSpan.Length;
            }

            ReadOnlySpan<char> noMetadataVersion = versionSpan.Slice(0, metadataIndex);
            int prereleaseIndex = noMetadataVersion.IndexOf('-');
            if (-1 == prereleaseIndex)
            {
                prereleaseIndex = metadataIndex;
            }

            return Version.TryParse(noMetadataVersion.Slice(0, prereleaseIndex).ToString(), out version);
        }

        private static ProcessInfo ParseCommon2(byte[] payload, ref int index)
        {
            ProcessInfo processInfo = ParseCommon(payload, ref index);

            processInfo.ManagedEntrypointAssemblyName = IpcHelpers.ReadString(payload, ref index);
            processInfo.ClrProductVersionString = IpcHelpers.ReadString(payload, ref index);

            return processInfo;
        }

        public ulong ProcessId { get; private set; }
        public Guid RuntimeInstanceCookie { get; private set; }
        public string CommandLine { get; private set; }
        public string OperatingSystem { get; private set; }
        public string ProcessArchitecture { get; private set; }
        public string ManagedEntrypointAssemblyName { get; private set; }
        public string ClrProductVersionString { get; private set; }
        public string PortableRuntimeIdentifier { get; private set; }
    }
}
