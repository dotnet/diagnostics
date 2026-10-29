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
        private const ulong AllocatedSectionFlag = 0x2; // SHF_ALLOC

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
            _elfFile = elfFile ?? throw new ArgumentNullException(nameof(elfFile));
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public ELFFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : this(tracer, new ELFFile(new StreamAddressSpace(file.Stream)), file.FileName)
        {
        }

        private bool IsOSSpecificType =>
            (ushort)_elfFile.Header.Type >= (ushort)ELFHeaderType.LowOS &&
            (ushort)_elfFile.Header.Type <= (ushort)ELFHeaderType.HighOS;

        public override bool IsValid()
        {
            return _elfFile.IsValid() &&
                (_elfFile.Header.Type == ELFHeaderType.Executable ||
                 _elfFile.Header.Type == ELFHeaderType.Shared ||
                 _elfFile.Header.Type == ELFHeaderType.Relocatable ||
                 IsOSSpecificType);
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                byte[] buildId = _elfFile.BuildID;
                if (NormalizeBuildId(ref buildId))
                {
                    bool hasLoadableImage;
                    bool hasDebugInfo = false;
                    try
                    {
                        hasLoadableImage = HasLoadableImage(out hasDebugInfo);
                    }
                    catch (Exception ex) when
                       (ex is InvalidVirtualAddressException ||
                        ex is ArgumentOutOfRangeException ||
                        ex is IndexOutOfRangeException ||
                        ex is BadInputFormatException ||
                        ex is OverflowException ||
                        ex is IOException)
                    {
                        Tracer.Verbose("ELF image classification failed for {0}: {1}", _path, ex.Message);
                        hasLoadableImage = false;
                        // Only standard ELF types can use the filename fallback.
                        if (!IsOSSpecificType)
                        {
                            hasDebugInfo = Array.IndexOf(s_symbolFileExtensions, Path.GetExtension(_path)) != -1;
                            hasLoadableImage = !hasDebugInfo;
                        }
                    }

                    if (IsOSSpecificType && !hasLoadableImage)
                    {
                        Tracer.Verbose("ELF file '{0}': cannot establish a loadable image for OS-specific ELF type 0x{1:x4}; module key generation is unsupported.",
                            _path, (ushort)_elfFile.Header.Type);
                    }

                    string symbolFileName = GetSymbolFileName();
                    SymbolStoreKey[] keys = GetKeys(flags, _path, buildId, hasLoadableImage, hasDebugInfo, symbolFileName).ToArray();
                    if (keys.Length == 0 && (flags & (KeyTypeFlags.ClrKeys | KeyTypeFlags.DacDbiKeys)) != 0)
                    {
                        string keyType = (flags & KeyTypeFlags.ClrKeys) != 0 ? "CLR" : "DAC/DBI";
                        Tracer.Verbose("ELF File `{0}`: file is not the coreclr module `{1}`. No {2} keys will be produced.", _path, CoreClrFileName, keyType);
                    }
                    foreach (SymbolStoreKey key in keys)
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
                        else
                        {
                            Tracer.Verbose("ELFfile `{0}`: header type is not an Executable. No host keys will be generated.", _path);
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
            return GetKeys(flags, path, buildId, hasLoadableImage: !symbolFile, hasDebugInfo: symbolFile, symbolFileName);
        }

        /// <summary>
        /// Creates ELF file symbol store keys using the independently detected image and debug capabilities.
        /// </summary>
        /// <param name="flags">type of keys to return</param>
        /// <param name="path">file name and path</param>
        /// <param name="buildId">ELF file uuid bytes</param>
        /// <param name="hasLoadableImage">whether the file contains a valid loadable image</param>
        /// <param name="hasDebugInfo">whether the file contains debug information</param>
        /// <param name="symbolFileName">name of symbol file (from .gnu_debuglink) or null</param>
        public static IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags, string path, byte[] buildId,
            bool hasLoadableImage, bool hasDebugInfo, string symbolFileName)
        {
            Debug.Assert(path != null);
            if (NormalizeBuildId(ref buildId))
            {
                string fileName = GetFileName(path);
                if (!hasLoadableImage && !hasDebugInfo)
                {
                    yield break;
                }
                bool debugCompanion = !hasLoadableImage && hasDebugInfo;

                if ((flags & KeyTypeFlags.IdentityKey) != 0)
                {
                    if (hasLoadableImage)
                    {
                        bool clrSpecialFile = s_coreClrSpecialFiles.Contains(fileName);
                        yield return BuildKey(path, IdentityPrefix, buildId, clrSpecialFile);
                    }
                    if (hasDebugInfo)
                    {
                        yield return BuildKey(path, SymbolPrefix, buildId, "_.debug");
                    }
                }
                if (!debugCompanion)
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
                    if ((flags & KeyTypeFlags.SymbolKey) != 0 && ((flags & KeyTypeFlags.IdentityKey) == 0 || !hasDebugInfo))
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

        private bool HasLoadableImage(out bool hasDebugInfo)
        {
            hasDebugInfo = false;
            ELFSection[] sections = null;
            if (_elfFile.Header.SectionHeaderOffset != 0 || _elfFile.Header.SectionHeaderCount != 0)
            {
                sections = _elfFile.Sections;
                hasDebugInfo = Array.Exists(sections, section =>
                    section.Name.StartsWith(".debug_info", StringComparison.Ordinal) ||
                    section.Name.StartsWith(".zdebug_info", StringComparison.Ordinal));
            }

            if (_elfFile.Header.Type != ELFHeaderType.Executable &&
                _elfFile.Header.Type != ELFHeaderType.Shared &&
                !IsOSSpecificType)
            {
                return false;
            }

            // Already-loaded images use memory layout, so skip on-disk validation.
            if (_elfFile.IsDataSourceVirtualAddressSpace)
            {
                return true;
            }

            List<ELFProgramSegment> loadSegments = new();
            bool hasLoadSegmentWithFileData = false;
            foreach (ELFProgramSegment segment in _elfFile.Segments)
            {
                if (segment.Header.Type == ELFProgramHeaderType.Load)
                {
                    if (!IsValidLoadSegment(segment))
                    {
                        return false;
                    }

                    if (segment.Header.FileSize > 0)
                    {
                        hasLoadSegmentWithFileData = true;
                    }
                    loadSegments.Add(segment);
                }
            }

            return hasLoadSegmentWithFileData && HasMappedModuleContents(sections, loadSegments);
        }

        private bool IsValidLoadSegment(ELFProgramSegment segment)
        {
            ulong fileOffset = segment.Header.FileOffset;
            ulong fileSize = segment.Header.FileSize;
            ulong virtualAddress = segment.Header.VirtualAddress;
            ulong memorySize = segment.Header.VirtualSize;
            ulong alignment = segment.Header.Alignment;
            // File data must fit in memory and within the file, the memory range must not overflow.
            // Alignment above 1 must be a power of two, with matching file and memory offsets.
            if (fileSize > memorySize ||
                fileOffset > _elfFile.DataSourceLength ||
                fileSize > _elfFile.DataSourceLength - fileOffset ||
                virtualAddress > ulong.MaxValue - memorySize ||
                (alignment > 1 && ((alignment & (alignment - 1)) != 0 || fileOffset % alignment != virtualAddress % alignment)))
            {
                return false;
            }

            return fileSize == 0 || IsReadableRange(segment.Contents, fileSize);
        }

        private bool HasMappedModuleContents(ELFSection[] sections, List<ELFProgramSegment> loadSegments)
        {
            // Without section headers, rely on the load segments already validated by the caller.
            if (sections is null)
            {
                return true;
            }

            foreach (ELFSection section in sections)
            {
                ELFSectionHeader sectionHeader = section.Header;
                ulong sectionSize = sectionHeader.FileSize;
                if ((sectionHeader.Flags & AllocatedSectionFlag) != 0 &&
                    sectionSize > 0 &&
                    sectionHeader.Type != ELFSectionHeaderType.NoBits &&
                    sectionHeader.Type != ELFSectionHeaderType.Note &&
                    sectionHeader.Type != ELFSectionHeaderType.Null &&
                    sectionHeader.FileOffset <= _elfFile.DataSourceLength &&
                    sectionSize <= _elfFile.DataSourceLength - sectionHeader.FileOffset &&
                    sectionHeader.VirtualAddress <= ulong.MaxValue - sectionSize)
                {
                    foreach (ELFProgramSegment segment in loadSegments)
                    {
                        ulong fileOffset = segment.Header.FileOffset;
                        ulong virtualAddress = segment.Header.VirtualAddress;
                        ulong fileSize = segment.Header.FileSize;
                        ulong memorySize = segment.Header.VirtualSize;
                        if (IsRangeContained(sectionHeader.FileOffset, sectionSize, fileOffset, fileSize) &&
                            IsRangeContained(sectionHeader.VirtualAddress, sectionSize, virtualAddress, memorySize) &&
                            sectionHeader.FileOffset - fileOffset == sectionHeader.VirtualAddress - virtualAddress &&
                            IsReadableRange(section.Contents, sectionSize))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool IsRangeContained(ulong start, ulong size, ulong containerStart, ulong containerSize)
        {
            return start >= containerStart &&
                start - containerStart <= containerSize &&
                size <= containerSize - (start - containerStart);
        }

        private static bool IsReadableRange(Reader reader, ulong size)
        {
            byte[] buffer = new byte[1];
            return reader.Read(0, buffer, 0, 1) == 1 && (size <= 1 || reader.Read(size - 1, buffer, 0, 1) == 1);
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
