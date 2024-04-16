// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.FileFormats;
using Microsoft.FileFormats.MachO;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class MachOFileKeyGenerator : KeyGenerator
    {
        /// <summary>
        /// The default symbol file extension used by .NET Core.
        /// </summary>
        private const string SymbolFileExtension = ".dwarf";

        private const string IdentityPrefix = "mach-uuid";
        private const string SymbolPrefix = "mach-uuid-sym";
        private const string CoreClrPrefix = "mach-uuid-coreclr";
        private const string CoreClrFileName = "libcoreclr.dylib";

        private static readonly string[] s_specialFiles = new string[] { "libmscordaccore.dylib", "libmscordbi.dylib" };
        private static readonly string[] s_sosSpecialFiles = new string[] { "libsos.dylib", "SOS.NETCore.dll" };

        private static readonly HashSet<string> s_coreClrSpecialFiles = new(s_specialFiles.Concat(s_sosSpecialFiles));
        private static readonly HashSet<string> s_dacdbiSpecialFiles = new(s_specialFiles);

        private readonly MachOFile _machoFile;
        private readonly string _path;

        public MachOFileKeyGenerator(ITracer tracer, MachOFile machoFile, string path)
            : base(tracer)
        {
            _machoFile = machoFile;
            _path = path;
        }

        public MachOFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : this(tracer, new MachOFile(new StreamAddressSpace(file.Stream)), file.FileName)
        {
        }

        public override bool IsValid()
        {
            return _machoFile.IsValid() &&
                (_machoFile.Header.FileType == MachHeaderFileType.Execute ||
                 _machoFile.Header.FileType == MachHeaderFileType.Dylib ||
                 _machoFile.Header.FileType == MachHeaderFileType.Dsym ||
                 _machoFile.Header.FileType == MachHeaderFileType.Bundle);
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                byte[] uuid = _machoFile.Uuid;
                if (uuid != null && uuid.Length == 16)
                {
                    bool symbolFile = _machoFile.Header.FileType == MachHeaderFileType.Dsym;
                    // TODO - mikem 1/23/18 - is there a way to get the name of the "linked" dwarf symbol file
                    foreach (SymbolStoreKey key in GetKeys(flags, _path, uuid, symbolFile, symbolFileName: null))
                    {
                        yield return key;
                    }
                    if ((flags & KeyTypeFlags.HostKeys) != 0)
                    {
                        if (_machoFile.Header.FileType == MachHeaderFileType.Execute)
                        {
                            // The host program as itself (usually dotnet)
                            yield return BuildKey(_path, IdentityPrefix, uuid);

                            // apphost downloaded as the host program name
                            yield return BuildKey(_path, IdentityPrefix, uuid, "apphost");
                        }
                    }
                }
                else
                {
                    Tracer.Error("Invalid MachO uuid {0}", _path);
                }
            }
        }

        /// <summary>
        /// Creates the MachO file symbol store keys.
        /// </summary>
        /// <param name="flags">type of keys to return</param>
        /// <param name="path">file name and path</param>
        /// <param name="uuid">macho file uuid bytes</param>
        /// <param name="symbolFile">if true, use the symbol file tag</param>
        /// <param name="symbolFileName">name of symbol file or null</param>
        /// <returns>symbol store keys</returns>
        public static IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags, string path, byte[] uuid, bool symbolFile, string symbolFileName)
        {
            Debug.Assert(path != null);
            Debug.Assert(uuid != null && uuid.Length == 16);

            string fileName = GetFileName(path);

            if ((flags & KeyTypeFlags.IdentityKey) != 0)
            {
                if (symbolFile)
                {
                    yield return BuildKey(path, SymbolPrefix, uuid, "_.dwarf");
                }
                else
                {
                    bool clrSpecialFile = s_coreClrSpecialFiles.Contains(fileName);
                    yield return BuildKey(path, IdentityPrefix, uuid, clrSpecialFile);
                }
            }
            if (!symbolFile)
            {
                if ((flags & KeyTypeFlags.RuntimeKeys) != 0 && fileName == CoreClrFileName)
                {
                    yield return BuildKey(path, IdentityPrefix, uuid);
                }
                if ((flags & KeyTypeFlags.SymbolKey) != 0)
                {
                    if (string.IsNullOrEmpty(symbolFileName))
                    {
                        symbolFileName = path + SymbolFileExtension;
                    }
                    yield return BuildKey(symbolFileName, SymbolPrefix, uuid, "_.dwarf");
                }
                if ((flags & (KeyTypeFlags.ClrKeys | KeyTypeFlags.DacDbiKeys)) != 0)
                {
                    /// Creates all the special CLR keys if the path is the coreclr module for this platform
                    if (fileName == CoreClrFileName)
                    {
                        foreach (string specialFileName in (flags & KeyTypeFlags.ClrKeys) != 0 ? s_coreClrSpecialFiles : s_dacdbiSpecialFiles)
                        {
                            yield return BuildKey(specialFileName, CoreClrPrefix, uuid);
                        }
                    }
                }
            }
        }
    }
}
