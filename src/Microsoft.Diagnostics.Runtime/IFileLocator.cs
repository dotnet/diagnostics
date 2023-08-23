// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// An implementation of
    /// </summary>
    public interface IFileLocator
    {
        /// <summary>
        /// Locates a PE Image archived under the given properties.
        /// </summary>
        /// <param name="fileName">The file name or path of the binary to locate.</param>
        /// <param name="buildTimeStamp">The build timestamp the binary is indexed under.</param>
        /// <param name="imageSize">The image size the binary is indexed under.</param>
        /// <param name="checkProperties">Whether or not to validate the properties of the binary after download.</param>
        /// <returns>A full path on disk (local) of where the binary was copied to or <see langword="null"/> if it was not found.</returns>
        string? FindPEImage(string fileName, int buildTimeStamp, int imageSize, bool checkProperties);

        /// <summary>
        /// Locates a PE Image potentially archived under an ELF or Mach-O binary's properties.
        /// </summary>
        /// <param name="fileName">The file name or path of the binary to locate.</param>
        /// <param name="archivedUnder">The file or keyword that this binary is archived under.</param>
        /// <param name="buildIdOrUUID">The buildId or UUID of the binary specified by <paramref name="archivedUnder"/>.</param>
        /// <param name="originalPlatform">The platform of the binary specified by <paramref name="archivedUnder"/>.</param>
        /// <param name="checkProperties">Whether or not to validate the properties of the binary after download.</param>
        /// <returns>A full path on disk (local) of where the binary was copied to or <see langword="null"/> if it was not found.</returns>
        string? FindPEImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildIdOrUUID, OSPlatform originalPlatform, bool checkProperties);

        /// <summary>
        /// Locates an Elf binary.
        /// </summary>
        /// <param name="fileName">The file name or path of the binary to locate.</param>
        /// <param name="archivedUnder">The file or keyword that this binary is archived under, <see langword="null"/> if its archived under its own properties.</param>
        /// <param name="buildId">The buildId of the Elf image to locate or the buildId of the image specified by <paramref name="archivedUnder"/>.</param>
        /// <param name="checkProperties">Whether or not to validate that the given file matches the build id.</param>
        /// <returns>A full path on disk (local) of where the binary was copied to or <see langword="null"/> if it was not found.</returns>
        string? FindElfImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildId, bool checkProperties);

        /// <summary>
        ///
        /// </summary>
        /// <param name="fileName">The file name or path of the binary to locate.</param>
        /// <param name="archivedUnder">The file or keyword that this binary is archived under, <see langword="null"/> if its archived under its own properties.</param>
        /// <param name="uuid">The UUID of the image or of the image specified by <paramref name="archivedUnder"/>.</param>
        /// <param name="checkProperties">Whether or not to validate that the given file matches the uuid.</param>
        /// <returns>A full path on disk (local) of where the binary was copied to or <see langword="null"/> if it was not found.</returns>
        string? FindMachOImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> uuid, bool checkProperties);
    }
}