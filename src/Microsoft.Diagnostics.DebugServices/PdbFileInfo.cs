// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Information about a specific PDB instance obtained from a PE image.
    /// </summary>
    public sealed class PdbFileInfo
    {
        /// <summary>
        /// Gets the Guid of the PDB.
        /// </summary>
        public Guid Guid { get; }

        /// <summary>
        /// Gets the PDB revision.
        /// </summary>
        public int Revision { get; }

        /// <summary>
        /// Gets the path to the PDB.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// True if portable PDB, false Windows
        /// </summary>
        public bool IsPortable { get; }

        /// <summary>
        /// Creates an instance of the PdbInfo with the corresponding properties initialized.
        /// </summary>
        public PdbFileInfo(string path, Guid guid, int revision, bool isPortable)
        {
            Path = path;
            Guid = guid;
            Revision = revision;
            IsPortable = isPortable;
        }

        public override string ToString() => $"{Guid} {Revision} {(IsPortable ? "(portable) " : string.Empty)}{Path}";
    }
}
