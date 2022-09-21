// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats.PE;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public static class Utilities
    {
        /// <summary>
        /// Combines two hash codes into a single hash code, in an order-dependent manner.
        /// </summary>
        /// <remarks>
        /// This function is neither commutative nor associative; the hash codes must be combined in
        /// a deterministic order.  Do not use this when hashing collections whose contents are
        /// nondeterministically ordered!
        /// </remarks>
        public static int CombineHashCodes(int hashCode0, int hashCode1)
        {
            unchecked {
                // This specific hash function is based on the Boost C++ library's CombineHash function:
                // http://stackoverflow.com/questions/4948780/magic-numbers-in-boosthash-combine
                // http://www.boost.org/doc/libs/1_46_1/doc/html/hash/combine.html 
                return hashCode0 ^ (hashCode1 + (int) 0x9e3779b9 + (hashCode0 << 6) + (hashCode0 >> 2));
            }
        }

        /// <summary>
        /// Convert from symstore VsFixedFileInfo to DebugServices VersionData
        /// </summary>
        public static VersionData ToVersionData(this VsFixedFileInfo fileInfo)
        { 
            return new VersionData(fileInfo.FileVersionMajor, fileInfo.FileVersionMinor, fileInfo.FileVersionRevision, fileInfo.FileVersionBuild);
        }

        /// <summary>
        /// Convert from clrmd VersionInfo to DebugServices VersionData
        /// </summary>
        public static VersionData ToVersionData(this Microsoft.Diagnostics.Runtime.VersionInfo versionInfo)
        { 
            return new VersionData(versionInfo.Major, versionInfo.Minor, versionInfo.Revision, versionInfo.Patch);
        }

        /// <summary>
        /// Convert from DebugServices VersionData to clrmd VersionInfo
        /// </summary>
        public static Microsoft.Diagnostics.Runtime.VersionInfo ToVersionInfo(this VersionData versionData)
        { 
            return new Microsoft.Diagnostics.Runtime.VersionInfo(versionData.Major, versionData.Minor, versionData.Revision, versionData.Patch);
        }

        /// <summary>
        /// Convert from symstore PEPdbRecord to DebugServices PdbFileInfo
        /// </summary>
        public static PdbFileInfo ToPdbFileInfo(this PEPdbRecord pdbInfo)
        {
            return new PdbFileInfo(pdbInfo.Path, pdbInfo.Signature, pdbInfo.Age, pdbInfo.IsPortablePDB);
        }

        /// <summary>
        /// Opens and returns an PEReader instance from the local file path
        /// </summary>
        /// <param name="filePath">PE file to open</param>
        /// <returns>PEReader instance or null</returns>
        public static PEReader OpenPEReader(string filePath)
        {
            Stream stream = TryOpenFile(filePath);
            if (stream is not null)
            {
                try
                {
                    var reader = new PEReader(stream);
                    if (reader.PEHeaders == null || reader.PEHeaders.PEHeader == null)
                    {
                        Trace.TraceWarning($"OpenPEReader: PEReader invalid headers");
                        return null;
                    }
                    return reader;
                }
                catch (Exception ex) when (ex is BadImageFormatException || ex is IOException)
                {
                    Trace.TraceError($"OpenPEReader: PEReader exception {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Attempt to open a file stream.
        /// </summary>
        /// <param name="path">file path</param>
        /// <returns>stream or null if doesn't exist or error</returns>
        public static Stream TryOpenFile(string path)
        {
            if (path is not null && File.Exists(path))
            {
                try
                {
                    return File.OpenRead(path);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is NotSupportedException || ex is IOException)
                {
                    Trace.TraceError($"TryOpenFile: {ex.Message}");
                }
            }
            return null;
        }
    }
}
