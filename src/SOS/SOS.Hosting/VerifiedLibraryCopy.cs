// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;

namespace SOS.Hosting
{
    /// <summary>
    /// A private copy of a DAC/DBI in a new random directory under the temp path. The signature is verified
    /// against and the library is loaded from this copy so the original file (which may be on a share or
    /// otherwise writable by others) can't be swapped between verification and load.
    /// </summary>
    internal sealed class VerifiedLibraryCopy : IDisposable
    {
        private readonly string _directory;

        public string FilePath { get; }

        private VerifiedLibraryCopy(string directory, string filePath)
        {
            _directory = directory;
            FilePath = filePath;
        }

        public static VerifiedLibraryCopy Create(string sourcePath)
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);

            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "dac.dll";
            }

            string destination = Path.Combine(directory, fileName);
            bool success = false;
            try
            {
                using (FileStream source = File.OpenRead(sourcePath))
                using (FileStream destinationStream = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(destinationStream);
                }
                success = true;
            }
            finally
            {
                if (!success)
                {
                    TryDeleteFile(destination);
                    TryDeleteDirectory(directory);
                }
            }

            return new VerifiedLibraryCopy(directory, destination);
        }

        /// <summary>
        /// Deletes the copy. This fails (and is only traced) while the library is still loaded.
        /// </summary>
        public void Dispose()
        {
            TryDeleteFile(FilePath);
            TryDeleteDirectory(_directory);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Could not delete temporary file '{path}': {ex.Message}");
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                Directory.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Could not delete temporary directory '{path}': {ex.Message}");
            }
        }
    }
}
