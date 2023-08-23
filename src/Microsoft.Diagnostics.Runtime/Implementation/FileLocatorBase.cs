// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    /// <summary>
    /// A base file locator, each method only builds the key under which a file is located.
    /// </summary>
    internal abstract class FileLocatorBase : IFileLocator
    {
        private const string MachOPlatformKey = "mach-uuid";
        private const string ElfPlatformKey = "elf-buildid";

        public virtual string? FindElfImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildId, bool checkProperties) => GetUnixKey(ElfPlatformKey, fileName, archivedUnder, buildId);
        public virtual string? FindMachOImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> uuid, bool checkProperties) => GetUnixKey(MachOPlatformKey, fileName, archivedUnder, uuid);
        public virtual string? FindPEImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildIdOrUUID, OSPlatform originalPlatform, bool checkProperties)
        {
            string osKey;

            if (originalPlatform == OSPlatform.OSX)
                osKey = MachOPlatformKey;
            else if (originalPlatform == OSPlatform.Linux)
                osKey = ElfPlatformKey;
            else
                return null;

            return GetUnixKey(osKey, fileName, archivedUnder, buildIdOrUUID);
        }

        public virtual string? FindPEImage(string fileName, int buildTimeStamp, int imageSize, bool checkProperties)
        {
            fileName = Path.GetFileName(fileName).ToLowerInvariant();
            return $"{fileName}\\{unchecked((uint)buildTimeStamp):x8}{unchecked((uint)imageSize):x}\\{fileName}";
        }

        private static string? GetUnixKey(string osKey, string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildId)
        {
            if (buildId.IsDefaultOrEmpty)
                return null;

            fileName = Path.GetFileName(fileName).ToLowerInvariant();
            string specialKey = "";
            if (archivedUnder == SymbolProperties.Coreclr)
                specialKey = "coreclr-";

            string id = string.Join("", buildId.Select(b => b.ToString("x2")));
            return $"{fileName}/{osKey}-{specialKey}{id}/{fileName}";
        }
    }
}
