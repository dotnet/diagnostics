// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class FileSymbolCache : FileLocatorBase
    {
        private readonly Dictionary<string, Task> _writingTo = new(GetEqualityComparer());

        public static bool IsCaseInsensitiveFileSystem => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        private static StringComparer GetEqualityComparer()
        {
            if (IsCaseInsensitiveFileSystem)
                return StringComparer.OrdinalIgnoreCase;

            return StringComparer.Ordinal;
        }

        public string Location { get; }

        public FileSymbolCache(string cacheLocation)
        {
            if (string.IsNullOrWhiteSpace(cacheLocation))
                throw new ArgumentNullException(nameof(cacheLocation));

            if (!Directory.Exists(cacheLocation))
                throw new DirectoryNotFoundException($"No symbol cache directory found at '{cacheLocation}'.");

            Location = cacheLocation;
        }

        public override string? FindElfImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildId, bool checkProperties)
        {
            if (Path.GetFileName(fileName) != fileName && File.Exists(fileName))
            {
                if (!checkProperties || buildId.IsDefaultOrEmpty)
                    return fileName;

                using ElfFile elf = new(fileName);
                if (elf.BuildId.SequenceEqual(buildId))
                    return fileName;
            }

            string? key = base.FindElfImage(fileName, archivedUnder, buildId, checkProperties);
            return FindImage(key);
        }

        public override string? FindMachOImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> uuid, bool checkProperties)
        {
            if (Path.GetFileName(fileName) != fileName && File.Exists(fileName))
            {
                if (!checkProperties || uuid.IsDefaultOrEmpty)
                    return fileName;

                // TODO:  We don't have a mach-o file reader to grab the uuid to verify.
                return fileName;
            }

            string? key = base.FindMachOImage(fileName, archivedUnder, uuid, checkProperties);
            return FindImage(key);
        }

        public override string? FindPEImage(string fileName, int buildTimeStamp, int imageSize, bool checkProperties)
        {
            if (Path.GetFileName(fileName) != fileName && File.Exists(fileName))
            {
                if (checkProperties)
                {
                    using PEImage peImage = new(File.OpenRead(fileName), leaveOpen: false);
                    if (peImage.IndexFileSize == imageSize && peImage.IndexTimeStamp == buildTimeStamp)
                        return fileName;
                }
            }

            string? key = base.FindPEImage(fileName, buildTimeStamp, imageSize, checkProperties);
            string? image = FindImage(key);

            if (checkProperties && image != null)
            {
                using PEImage peImage = new(File.OpenRead(image), leaveOpen: false);
                if (peImage.IndexFileSize != imageSize || peImage.IndexTimeStamp != buildTimeStamp)
                    image = null;
            }

            return image;
        }

        public override string? FindPEImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildIdOrUUID, OSPlatform platform, bool checkProperties)
        {
            string? key = base.FindPEImage(fileName, archivedUnder, buildIdOrUUID, platform, checkProperties);
            return FindImage(key);
        }

        private string? FindImage(string? key)
        {
            if (key is null)
                return null;

            string fullPath = Path.Combine(Location, key);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
            else
            {
                string fileName = Path.GetFileName(fullPath);
                fullPath = Path.Combine(Location, fileName);
                return File.Exists(fullPath) ? fullPath : null;
            }
        }

        internal string Store(Stream stream, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"Cannot store to an empty {nameof(key)}.");

            string fullPath = Path.Combine(Location, key);

            TaskCompletionSource<int> source = new();
            Task? currentWrite = null;
            lock (_writingTo)
            {
                if (!_writingTo.TryGetValue(fullPath, out currentWrite))
                    _writingTo.Add(fullPath, source.Task);
            }

            if (currentWrite != null)
            {
                // just in case
                source.SetResult(0);

                currentWrite.Wait();
                return fullPath;
            }

            try
            {
                string directory = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(directory);
                using FileStream fs = File.Create(fullPath);
                stream.CopyTo(fs);
                return fullPath;
            }
            catch (Exception ex)
            {
                source.SetException(ex);
                throw;
            }
            finally
            {
                source.SetResult(0);

                // We don't remove the entry from _writingTo.  If we removed it: We could race between checking if a file
                // exists and Store, meaning two threads see the file is missing, both attempt to store it, one thread
                // completes this method and the other will overwrite that file again.  A third thread might see the file
                // exists and tries to open the half written file.  Better to just 'leak' the path.
            }
        }
    }
}