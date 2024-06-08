// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class ELFFileKeyGenerator : KeyGenerator
    {
        private const string IdentityPrefix = "elf-buildid";
        private const string SymbolPrefix = "elf-buildid-sym";
        private const string CoreClrPrefix = "elf-buildid-coreclr";
        private const string CoreClrFileName = "libcoreclr.so";

        /// <summary>
        /// Symbol file extensions. The first one is the default symbol file extension used by .NET Core.
        /// </summary>
        private static readonly string[] s_symbolFileExtensions = { ".dbg", ".debug" };

        /// <summary>
        /// List of special clr files that are also indexed with libcoreclr.so's key.
        /// </summary>
        private static readonly string[] s_specialFiles = new string[] { "libmscordaccore.so", "libmscordbi.so", "mscordaccore.dll", "mscordbi.dll" };
        private static readonly string[] s_sosSpecialFiles = new string[] { "libsos.so", "SOS.NETCore.dll" };

        private static readonly HashSet<string> s_coreClrSpecialFiles = new(s_specialFiles.Concat(s_sosSpecialFiles));
        private static readonly HashSet<string> s_dacdbiSpecialFiles = new(s_specialFiles);

        private readonly ELFFile _elfFile;
        private readonly string _path;

        public ELFFileKeyGenerator(ITracer tracer, ELFFile elfFile, string path)
            : base(tracer)
        {
            _elfFile = elfFile;
            _path = path;
        }

        public ELFFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : this(tracer, new ELFFile(new StreamAddressSpace(file.Stream)), file.FileName)
        {
        }

        public override bool IsValid()
        {
            return _elfFile.IsValid() &&
                (_elfFile.Header.Type == ELFHeaderType.Executable || _elfFile.Header.Type == ELFHeaderType.Shared || _elfFile.Header.Type == ELFHeaderType.Relocatable);
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                byte[] buildId = _elfFile.BuildID;
                if (NormalizeBuildId(ref buildId))
                {
                    bool symbolFile = false;
                    try
                    {
                        symbolFile = Array.Exists(_elfFile.Sections, section => (section.Name.StartsWith(".debug_info") || section.Name.StartsWith(".zdebug_info")));
                    }
                    catch (Exception ex) when
                       (ex is InvalidVirtualAddressException ||
                        ex is ArgumentOutOfRangeException ||
                        ex is IndexOutOfRangeException ||
                        ex is BadInputFormatException)
                    {
                        // This could occur when trying to read sections for an ELF image grabbed from a core dump
                        // In that case, fallback to checking the file extension
                        symbolFile = Array.IndexOf(s_symbolFileExtensions, Path.GetExtension(_path)) != -1;
                    }

                    string symbolFileName = GetSymbolFileName();
                    foreach (SymbolStoreKey key in GetKeys(flags, _path, buildId, symbolFile, symbolFileName))
                    {
                        yield return key;
                    }
                    if ((flags & KeyTypeFlags.HostKeys) != 0)
                    {
                        if (_elfFile.Header.Type == ELFHeaderType.Executable)
                        {
                            // The host program as itself (usually dotnet)
                            yield return BuildKey(_path, IdentityPrefix, buildId);

                            // apphost downloaded as the host program name
                            yield return BuildKey(_path, IdentityPrefix, buildId, "apphost");
                        }
                    }
                }
                else
                {
                    Tracer.Error("Invalid ELF BuildID '{0}' for {1}", buildId == null ? "<null>" : ToHexString(buildId), _path);
                }
            }
        }

        /// <summary>
        /// Creates the ELF file symbol store keys.
        /// </summary>
        /// <param name="flags">type of keys to return</param>
        /// <param name="path">file name and path</param>
        /// <param name="buildId">ELF file uuid bytes</param>
        /// <param name="symbolFile">if true, use the symbol file tag</param>
        /// <param name="symbolFileName">name of symbol file (from .gnu_debuglink) or null</param>
        /// <returns>symbol store keys</returns>
        public static IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags, string path, byte[] buildId, bool symbolFile, string symbolFileName)
        {
            Debug.Assert(path != null);
            if (NormalizeBuildId(ref buildId))
            {
                string fileName = GetFileName(path);

                if ((flags & KeyTypeFlags.IdentityKey) != 0)
                {
                    if (symbolFile)
                    {
                        yield return BuildKey(path, SymbolPrefix, buildId, "_.debug");
                    }
                    else
                    {
                        bool clrSpecialFile = s_coreClrSpecialFiles.Contains(fileName);
                        yield return BuildKey(path, IdentityPrefix, buildId, clrSpecialFile);
                    }
                }
                if (!symbolFile)
                {
                    // This is a workaround for 5.0 where the ELF file type of dotnet isn't Executable but
                    // Shared. It doesn't work for self-contained apps (apphost renamed to host program).
                    if ((flags & KeyTypeFlags.HostKeys) != 0 && fileName == "dotnet")
                    {
                        yield return BuildKey(path, IdentityPrefix, buildId, clrSpecialFile: false);
                    }
                    if ((flags & KeyTypeFlags.RuntimeKeys) != 0 && fileName == CoreClrFileName)
                    {
                        yield return BuildKey(path, IdentityPrefix, buildId);
                    }
                    if ((flags & KeyTypeFlags.SymbolKey) != 0)
                    {
                        if (string.IsNullOrEmpty(symbolFileName))
                        {
                            symbolFileName = path + s_symbolFileExtensions[0];
                        }
                        yield return BuildKey(symbolFileName, SymbolPrefix, buildId, "_.debug");
                    }
                    if ((flags & (KeyTypeFlags.ClrKeys | KeyTypeFlags.DacDbiKeys)) != 0)
                    {
                        // Creates all the special CLR keys if the path is the coreclr module for this platform
                        if (fileName == CoreClrFileName)
                        {
                            foreach (string specialFileName in (flags & KeyTypeFlags.ClrKeys) != 0 ? s_coreClrSpecialFiles : s_dacdbiSpecialFiles)
                            {
                                yield return BuildKey(specialFileName, CoreClrPrefix, buildId);
                            }
                        }
                    }
                }
            }
            else
            {
                Debug.Fail($"Invalid ELF BuildId '{(buildId == null ? "<null>" : ToHexString(buildId))}' for {path}");
            }
        }

        private string GetSymbolFileName()
        {
            try
            {
                ELFSection section = _elfFile.FindSectionByName(".gnu_debuglink");
                if (section != null)
                {
                    return section.Contents.Read<string>(0);
                }
            }
            catch (Exception ex) when
               (ex is InvalidVirtualAddressException ||
                ex is ArgumentOutOfRangeException ||
                ex is IndexOutOfRangeException ||
                ex is BadInputFormatException)
            {
                Tracer.Verbose("ELF .gnu_debuglink section in {0}: {1}", _path, ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Extends build-ids of 8-20 bytes (created by MD5 or UUID build ids) to 20 bytes with proper padding
        /// using a zero extension
        /// </summary>
        /// <param name="buildId">Reference to ELF build-id. This build-id must be between 8 and 20 bytes in length.</param>
        /// <returns>True if the build-id is compliant and could be resized and padded. False otherwise.</returns>
        private static bool NormalizeBuildId(ref byte[] buildId)
        {
            if (buildId == null || buildId.Length > 20 || buildId.Length < 8)
            {
                return false;
            }
            int oldLength = buildId.Length;
            Array.Resize(ref buildId, 20);
            for (int i = oldLength; i < buildId.Length; i++)
            {
                buildId[i] = 0;
            }
            return true;
        }
    }
}
