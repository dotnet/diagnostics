// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime
{
    internal static class UnixDataReaderExtensions
    {

        [return: NotNullIfNotNull(nameof(version))]
        internal static bool GetVersionInfo(this IDataReader dataReader, ulong baseAddress, ElfFile loadedFile, out System.Version? version)
        {
            foreach (ElfProgramHeader programHeader in loadedFile.ProgramHeaders)
            {
                if (programHeader.Type == ElfProgramHeaderType.Load && programHeader.IsWritable)
                    return GetVersionInfo(dataReader, baseAddress + programHeader.VirtualAddress, programHeader.VirtualSize, out version);
            }

            version = null;
            return false;
        }

        [return: NotNullIfNotNull(nameof(version))]
        internal static bool GetVersionInfo(this IDataReader dataReader, ulong startAddress, ulong size, out System.Version? version)
        {
            // (int)size underflow will result in returning 0 here, so this is acceptable
            ulong address = dataReader.SearchMemory(startAddress, (int)size, PlatformFunctions.s_versionString);
            if (address == 0)
            {
                version = null;
                return false;
            }

            Span<byte> bytes = stackalloc byte[64];
            int read = dataReader.Read(address + (uint)PlatformFunctions.s_versionLength, bytes);
            if (read > 0)
            {
                bytes = bytes.Slice(0, read);
                version = ParseAsciiVersion(bytes);
                return true;
            }

            version = null;
            return false;
        }

        private static System.Version? ParseAsciiVersion(ReadOnlySpan<byte> span)
        {
            int major = 0, minor = 0, rev = 0, patch = 0;

            int position = 0;
            long curr = 0;

            for (int i = 0; ; i++)
            {
                if (i == span.Length || span[i] == '.' || span[i] == ' ')
                {
                    switch (position)
                    {
                        case 0:
                            major = (int)curr;
                            break;

                        case 1:
                            minor = (int)curr;
                            break;

                        case 2:
                            rev = (int)curr;
                            break;

                        case 3:
                            patch = (int)curr;
                            break;
                    }

                    curr = 0;
                    if (i == span.Length)
                        break;

                    if (++position == 4 || span[i] == ' ')
                        break;
                }

                // skip bits like "-beta"
                if (span[i] is >= (byte)'0' and <= (byte)'9')
                    curr = curr * 10 + (span[i] - '0');

                // In this case I don't know what we are parsing but it's not a version
                if (curr > int.MaxValue)
                    return null;
            }

            return new System.Version(major, minor, rev, patch);
        }
    }
}