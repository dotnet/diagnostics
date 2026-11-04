// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    internal static class ClrInfoConverter
    {
        public static ClrInfo Create(DataTarget dataTarget, ModuleInfo module, RuntimeDescription description) =>
            new(dataTarget, module, description.Version)
            {
                Flavor = description.RuntimeType switch
                {
                    RuntimeType.Desktop => ClrFlavor.Desktop,
                    RuntimeType.NetCore or RuntimeType.SingleFile => ClrFlavor.Core,
                    RuntimeType.NativeAOT => ClrFlavor.NativeAOT,
                    _ => throw new ArgumentException($"Unsupported runtime type: {description.RuntimeType}", nameof(description)),
                },
                IsSingleFile = description.IsSingleFile || description.RuntimeType == RuntimeType.SingleFile,
                IndexFileSize = description.IndexFileSize,
                IndexTimeStamp = description.IndexTimeStamp,
                BuildId = description.BuildId,
                ContractDescriptorAddress = description.ContractDescriptorAddress,
                DebuggingLibraries = description.DebuggingLibraries.Select(ConvertLibrary).Distinct()
                    .OrderBy(library => library.Kind)
                    .ThenBy(library => !PathUtilities.IsSafeAbsoluteLocalPath(library.FileName))
                    .ThenBy(library => library.ArchivedUnder)
                    .ToImmutableArray(),
            };

        private static DebugLibraryInfo ConvertLibrary(RuntimeDebuggingLibrary library)
        {
            DebugLibraryKind kind = library.Kind switch
            {
                RuntimeDebuggingLibraryKind.Dac => DebugLibraryKind.Dac,
                RuntimeDebuggingLibraryKind.Dbi => DebugLibraryKind.Dbi,
                RuntimeDebuggingLibraryKind.CDac => DebugLibraryKind.CDac,
                _ => throw new ArgumentException($"Unsupported debugging library kind: {library.Kind}", nameof(library)),
            };
            SymbolProperties archivedUnder = library.ArchivedUnder switch
            {
                RuntimeDebuggingLibraryIndex.None => SymbolProperties.None,
                RuntimeDebuggingLibraryIndex.Runtime => SymbolProperties.Coreclr,
                RuntimeDebuggingLibraryIndex.Self => SymbolProperties.Self,
                _ => throw new ArgumentException($"Unsupported debugging library index: {library.ArchivedUnder}", nameof(library)),
            };
            return library.Platform == OSPlatform.Windows && library.BuildId.IsDefaultOrEmpty
                ? new DebugLibraryInfo(kind, library.FileName, library.TargetArchitecture, archivedUnder, library.IndexFileSize, library.IndexTimeStamp)
                : new DebugLibraryInfo(kind, library.FileName, library.TargetArchitecture, library.Platform, archivedUnder, library.BuildId);
        }

    }
}
