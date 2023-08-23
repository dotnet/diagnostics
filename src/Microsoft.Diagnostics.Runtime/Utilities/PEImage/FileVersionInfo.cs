// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// FileVersionInfo represents the extended version formation that is optionally placed in the PE file resource area.
    /// </summary>
    internal sealed unsafe class FileVersionInfo
    {
        /// <summary>
        /// Gets the version string
        /// </summary>
        public string? FileVersion { get; }

        /// <summary>
        /// Gets the version of this module
        /// </summary>
        public System.Version? Version { get; }

        /// <summary>
        /// Gets comments to supplement the file version
        /// </summary>
        public string? Comments { get; }

        internal FileVersionInfo(ReadOnlySpan<byte> data)
        {
            ReadOnlySpan<char> dataAsString = MemoryMarshal.Cast<byte, char>(data);

            FileVersion = GetDataString(dataAsString, "FileVersion".AsSpan());
            Comments = GetDataString(dataAsString, "Comments".AsSpan());
            Version = GetVersionInfo(dataAsString);
        }

        private static System.Version? GetVersionInfo(ReadOnlySpan<char> dataAsString)
        {
            ReadOnlySpan<char> fileVersionKey = "VS_VERSION_INFO".AsSpan();
            int fileVersionIndex = dataAsString.IndexOf(fileVersionKey);
            if (fileVersionIndex < 0)
                return null;

            dataAsString = dataAsString.Slice(fileVersionIndex + fileVersionKey.Length);
            ReadOnlySpan<byte> asBytes = MemoryMarshal.Cast<char, byte>(dataAsString);

            int minor = MemoryMarshal.Read<ushort>(asBytes.Slice(12));
            int major = MemoryMarshal.Read<ushort>(asBytes.Slice(14));
            int revision = MemoryMarshal.Read<ushort>(asBytes.Slice(16));
            int build = MemoryMarshal.Read<ushort>(asBytes.Slice(18));

            return new System.Version(major, minor, build, revision);
        }

        private static string? GetDataString(ReadOnlySpan<char> dataAsString, ReadOnlySpan<char> fileVersionKey)
        {
            int fileVersionIndex = dataAsString.IndexOf(fileVersionKey);
            if (fileVersionIndex < 0)
                return null;

            dataAsString = dataAsString.Slice(fileVersionIndex + fileVersionKey.Length);
            dataAsString = dataAsString.TrimStart('\0');

            int endIndex = dataAsString.IndexOf('\0');
            if (endIndex < 0)
                return null;

            return dataAsString.Slice(0, endIndex).ToString();
        }

        public override string? ToString() => FileVersion;
    }
}
