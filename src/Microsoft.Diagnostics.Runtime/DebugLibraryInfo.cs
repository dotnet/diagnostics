// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// This class provides information about CLR debugging artifacts.
    /// </summary>
    public sealed class DebugLibraryInfo
    {
        /// <summary>
        /// The kind of debugging library this is.
        /// </summary>
        public DebugLibraryKind Kind { get; }

        /// <summary>
        /// Gets the platform specific filename of the debugging library.
        /// This may be a full path on disk if we find that this machine has the file locally.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Gets the architecture of this debugging library.
        /// </summary>
        public Architecture TargetArchitecture { get; }

        /// <summary>
        /// Returns what properties that this dac library is archived under.
        /// </summary>
        public SymbolProperties ArchivedUnder { get; }

        /// <summary>
        /// Gets the specific file size of the image used to index it on the symbol server.
        /// </summary>
        public int IndexFileSize { get; }

        /// <summary>
        /// Gets the timestamp of the image used to index it on the symbol server.
        /// </summary>
        public int IndexTimeStamp { get; }

        /// <summary>
        /// The BuildId that this library is indexed under (or IsEmptyOrDefault otherwise).
        /// </summary>
        public ImmutableArray<byte> IndexBuildId { get; }

        /// <summary>
        /// The platform that this library was designed to run on.
        /// </summary>
        public OSPlatform Platform { get; }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int code = FileName.GetHashCode() ^ IndexTimeStamp.GetHashCode() ^ IndexFileSize.GetHashCode();
            if (!IndexBuildId.IsDefaultOrEmpty)
            {
                foreach (byte b in IndexBuildId)
                    code ^= b;
            }

            return code;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            if (obj is not DebugLibraryInfo other)
                return false;

            if (!IndexBuildId.IsDefaultOrEmpty)
            {
                if (other.IndexBuildId.IsDefaultOrEmpty)
                    return false;

                if (!IndexBuildId.SequenceEqual(other.IndexBuildId))
                    return false;
            }

            return FileName == other.FileName && IndexTimeStamp == other.IndexTimeStamp && IndexFileSize == other.IndexFileSize;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (IndexBuildId.IsDefaultOrEmpty)
                return $"{FileName} size:{IndexFileSize:x} time:{IndexTimeStamp:x}";

            return $"{FileName} {string.Join("", IndexBuildId.Select(x => x.ToString("x")))}";
        }

        public DebugLibraryInfo(DebugLibraryKind kind, string fileName, Architecture targetArch, OSPlatform platform, SymbolProperties archivedUnder, ImmutableArray<byte> clrBuildId)
        {
            Kind = kind;
            FileName = fileName;
            TargetArchitecture = targetArch;
            ArchivedUnder = archivedUnder;
            IndexBuildId = clrBuildId;
            Platform = platform;
        }

        public DebugLibraryInfo(DebugLibraryKind kind, string fileName, Architecture targetArch, SymbolProperties archivedUnder, int fileSize, int timestamp)
        {
            Kind = kind;
            FileName = fileName;
            TargetArchitecture = targetArch;
            ArchivedUnder = archivedUnder;
            IndexFileSize = fileSize;
            IndexTimeStamp = timestamp;
            IndexBuildId = ImmutableArray<byte>.Empty;
            Platform = OSPlatform.Windows;
        }
    }
}
