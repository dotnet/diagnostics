// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Details about a module
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// Target for this module.
        /// </summary>
        ITarget Target { get; }

        /// <summary>
        /// The per module services like an optional clrmd's PEImage or PEReader instances if a PE module.
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Debugger specific module index
        /// </summary>
        int ModuleIndex { get; }

        /// <summary>
        /// Gets the file name of the module on disk.
        /// </summary>
        string FileName { get; }

        /// <summary>
        /// Gets the base address of the object.
        /// </summary>
        ulong ImageBase { get; }

        /// <summary>
        /// Returns the image size of module in memory.
        /// </summary>
        ulong ImageSize { get; }

        /// <summary>
        /// Gets the specific file size of the image used to index it on the symbol server.
        /// </summary>
        uint? IndexFileSize { get; }

        /// <summary>
        /// Gets the timestamp of the image used to index it on the symbol server.
        /// </summary>
        uint? IndexTimeStamp { get; }

        /// <summary>
        /// Build id on Linux and MacOS, otherwise empty value.
        /// </summary>
        ImmutableArray<byte> BuildId { get; }

        /// <summary>
        /// Returns true if Windows PE format image (native or IL).
        /// </summary>
        bool IsPEImage { get; }

        /// <summary>
        /// Returns true if managed or IL assembly.
        /// </summary>
        bool IsManaged { get; }

        /// <summary>
        /// Returns true if the PE module is layout is file. False, layout is loaded image. If null, not a PE image.
        /// </summary>
        bool? IsFileLayout { get; }

        /// <summary>
        /// Returns PDB information for Windows PE modules (managed or native).
        /// </summary>
        IEnumerable<PdbFileInfo> GetPdbFileInfos();

        /// <summary>
        /// Returns the Linux or MacOS symbol file name.
        /// </summary>
        string GetSymbolFileName();

        /// <summary>
        /// Returns the version information for the modules.
        /// </summary>
        Version GetVersionData();

        /// <summary>
        /// Returns the file version string containing the build version and commit id.
        /// </summary>
        string GetVersionString();

        /// <summary>
        /// Loads or downloads the module's symbol file and registers it with the underlying host debugger.
        /// </summary>
        /// <returns>the symbol file name</returns>
        string LoadSymbols();
    }
}
