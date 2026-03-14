// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.FileFormats.PDB
{
    /// <summary>
    /// An abstraction for reading both MSF (normal PDB files) and MSFZ files (compressed PDB files).
    /// </summary>
    internal interface IMSFFile
    {
        /// <summary>
        /// The number of streams stored in the file. This will always be at least 1.
        /// </summary>
        uint NumStreams { get; }

        /// <summary>
        /// Gets an object which can read the given stream.
        /// </summary>
        /// <param name="stream">The index of the stream. This must be less than NumStreams.</param>
        /// <returns>A Reader which can read the stream.</returns>
        Reader GetStream(uint stream);

        /// <summary>
        /// Returns the container kind for this implementation.
        /// </summary>
        PDBContainerKind ContainerKind { get; }

        /// <summary>
        /// Returns a string which identifies the container kind, using a backward-compatible naming scheme.
        /// <summary>
        /// <para>
        /// The existing PDB format is identified as "pdb", while PDZ (MSFZ) is identified as "msfz0".
        /// This allows new versions of MSFZ to be identified and deployed without updating clients of this API.
        /// </para>
        public string ContainerKindSpecString { get; }
    }
}
