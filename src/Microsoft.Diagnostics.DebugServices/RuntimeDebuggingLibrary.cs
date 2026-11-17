// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices
{
    public enum RuntimeDebuggingLibraryKind
    {
        Dac,
        Dbi,
        CDac,
    }

    public enum RuntimeDebuggingLibraryIndex
    {
        None,
        Runtime,
        Self,
    }

    /// <summary>
    /// A debugging library and its symbol-store identity, or an explicitly supplied local path.
    /// </summary>
    public sealed class RuntimeDebuggingLibrary
    {
        public static RuntimeDebuggingLibrary CreateWindows(
            RuntimeDebuggingLibraryKind kind,
            string fileName,
            Architecture targetArchitecture,
            RuntimeDebuggingLibraryIndex archivedUnder,
            int indexFileSize,
            int indexTimeStamp) =>
            new(kind, fileName, targetArchitecture, OSPlatform.Windows, archivedUnder,
                indexFileSize, indexTimeStamp, ImmutableArray<byte>.Empty);

        public static RuntimeDebuggingLibrary CreateWithBuildId(
            RuntimeDebuggingLibraryKind kind,
            string fileName,
            Architecture targetArchitecture,
            OSPlatform platform,
            RuntimeDebuggingLibraryIndex archivedUnder,
            ImmutableArray<byte> buildId) =>
            new(kind, fileName, targetArchitecture, platform, archivedUnder, 0, 0, buildId);

        private RuntimeDebuggingLibrary(
            RuntimeDebuggingLibraryKind kind,
            string fileName,
            Architecture targetArchitecture,
            OSPlatform platform,
            RuntimeDebuggingLibraryIndex archivedUnder,
            int indexFileSize,
            int indexTimeStamp,
            ImmutableArray<byte> buildId)
        {
            Kind = kind;
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            TargetArchitecture = targetArchitecture;
            Platform = platform;
            ArchivedUnder = archivedUnder;
            IndexFileSize = indexFileSize;
            IndexTimeStamp = indexTimeStamp;
            BuildId = buildId;
        }

        public RuntimeDebuggingLibraryKind Kind { get; }
        public string FileName { get; }
        public Architecture TargetArchitecture { get; }
        public OSPlatform Platform { get; }
        public RuntimeDebuggingLibraryIndex ArchivedUnder { get; }
        public int IndexFileSize { get; }
        public int IndexTimeStamp { get; }
        public ImmutableArray<byte> BuildId { get; }
    }
}
