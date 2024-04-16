﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.FileFormats;
using Microsoft.FileFormats.PE;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class PEFileKeyGenerator : KeyGenerator
    {
        private const string CoreClrFileName = "coreclr.dll";

        private static readonly HashSet<string> s_longNameBinaryPrefixes = new HashSet<string>(new string[] { "mscordaccore_", "sos_" });
        private static readonly HashSet<string> s_daclongNameBinaryPrefixes = new HashSet<string>(new string[] { "mscordaccore_" });

        private static readonly string[] s_specialFiles = new string[] { "mscordaccore.dll", "mscordbi.dll" };
        private static readonly string[] s_sosSpecialFiles = new string[] { "sos.dll", "SOS.NETCore.dll" };

        private static readonly HashSet<string> s_coreClrSpecialFiles = new HashSet<string>(s_specialFiles.Concat(s_sosSpecialFiles));
        private static readonly HashSet<string> s_dacdbiSpecialFiles = new HashSet<string>(s_specialFiles);

        private readonly PEFile _peFile;
        private readonly string _path;

        public PEFileKeyGenerator(ITracer tracer, PEFile peFile, string path)
            : base(tracer)
        {
            _peFile = peFile;
            _path = path;
        }

        public PEFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : this(tracer, new PEFile(new StreamAddressSpace(file.Stream)), file.FileName)
        {
        }

        public override bool IsValid()
        {
            return _peFile.IsValid();
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                if ((flags & KeyTypeFlags.IdentityKey) != 0)
                {
                    yield return GetKey(_path, _peFile.Timestamp, _peFile.SizeOfImage);
                }
                if ((flags & KeyTypeFlags.RuntimeKeys) != 0 && GetFileName(_path) == CoreClrFileName)
                {
                    yield return GetKey(_path, _peFile.Timestamp, _peFile.SizeOfImage);
                }
                if ((flags & KeyTypeFlags.SymbolKey) != 0)
                {
                    PEPdbRecord[] pdbs = new PEPdbRecord[0]; 
                    try
                    {
                        pdbs = _peFile.Pdbs.ToArray();
                    }
                    catch (InvalidVirtualAddressException ex)
                    {
                        Tracer.Error("Reading PDB records for {0}: {1}", _path, ex.Message);
                    }

                    foreach (PEPdbRecord pdb in pdbs)
                    {
                        if (((flags & KeyTypeFlags.ForceWindowsPdbs) == 0) && pdb.IsPortablePDB)
                        {
                            yield return PortablePDBFileKeyGenerator.GetKey(pdb.Path, pdb.Signature, _peFile.PdbChecksums);
                        }
                        else
                        {
                            yield return PDBFileKeyGenerator.GetKey(pdb.Path, pdb.Signature, pdb.Age, _peFile.PdbChecksums);
                        }
                    }
                }

                if ((flags & KeyTypeFlags.PerfMapKeys) != 0)
                {
                    foreach(PEPerfMapRecord perfmapRecord in _peFile.PerfMapsV1)
                    {
                        if (perfmapRecord.Version > FileFormats.PerfMap.PerfMapFile.MaxKnownPerfMapVersion)
                            Tracer.Warning("Trying to get key for PerfmapFile {0} associated with PE {1} with version {2}, higher than max known version {3}", 
                                perfmapRecord.Path, _path, perfmapRecord.Version, FileFormats.PerfMap.PerfMapFile.MaxKnownPerfMapVersion);
                        yield return PerfMapFileKeyGenerator.GetKey(perfmapRecord.Path, perfmapRecord.Signature, perfmapRecord.Version);
                    }
                }

                if ((flags & (KeyTypeFlags.ClrKeys | KeyTypeFlags.DacDbiKeys)) != 0)
                {
                    if (GetFileName(_path) == CoreClrFileName)
                    {
                        string coreclrId = string.Format("{0:X8}{1:x}", _peFile.Timestamp, _peFile.SizeOfImage);
                        foreach (string specialFileName in GetSpecialFiles(flags))
                        {
                            yield return BuildKey(specialFileName, coreclrId);
                        }
                    }
                }
                if ((flags & KeyTypeFlags.HostKeys) != 0)
                {
                    if ((_peFile.FileHeader.Characteristics & (ushort)ImageFile.Dll) == 0 && !_peFile.IsILImage)
                    {
                        string id = string.Format("{0:X8}{1:x}", _peFile.Timestamp, _peFile.SizeOfImage);

                        // The host program as itself (usually dotnet.exe)
                        yield return BuildKey(_path, id);

                        // apphost.exe downloaded as the host program name
                        yield return BuildKey(_path, prefix: null, id, "apphost.exe");
                    }
                }
            }
        }

        private IEnumerable<string> GetSpecialFiles(KeyTypeFlags flags)
        {
            var specialFiles = new List<string>((flags & KeyTypeFlags.ClrKeys) != 0 ? s_coreClrSpecialFiles : s_dacdbiSpecialFiles);

            VsFixedFileInfo fileVersion = _peFile.VersionInfo;
            if (fileVersion != null)
            {
                ushort major = fileVersion.ProductVersionMajor;
                ushort minor = fileVersion.ProductVersionMinor;
                ushort build = fileVersion.ProductVersionBuild;
                ushort revision = fileVersion.ProductVersionRevision;

                var hostArchitectures = new List<string>();
                string targetArchitecture = null;

                ImageFileMachine machine = (ImageFileMachine)_peFile.FileHeader.Machine;
                switch (machine)
                {
                    case ImageFileMachine.Amd64:
                        targetArchitecture = "amd64";
                        break;

                    case ImageFileMachine.I386:
                        targetArchitecture = "x86";
                        break;

                    case ImageFileMachine.ArmNT:
                        targetArchitecture = "arm";
                        hostArchitectures.Add("x86");
                        break;

                    case ImageFileMachine.Arm64:
                        targetArchitecture = "arm64";
                        hostArchitectures.Add("amd64");
                        break;
                }

                if (targetArchitecture != null)
                {
                    hostArchitectures.Add(targetArchitecture);

                    foreach (string hostArchitecture in hostArchitectures)
                    {
                        string buildFlavor = "";

                        if ((fileVersion.FileFlags & FileInfoFlags.Debug) != 0)
                        {
                            if ((fileVersion.FileFlags & FileInfoFlags.SpecialBuild) != 0)
                            {
                                buildFlavor = ".dbg";
                            }
                            else
                            {
                                buildFlavor = ".chk";
                            }
                        }

                        foreach (string name in (flags & KeyTypeFlags.ClrKeys) != 0 ? s_longNameBinaryPrefixes : s_daclongNameBinaryPrefixes)
                        {
                            // The name prefixes include the trailing "_".
                            string longName = string.Format("{0}{1}_{2}_{3}.{4}.{5}.{6:00}{7}.dll",
                                name, hostArchitecture, targetArchitecture, major, minor, build, revision, buildFlavor);
                            specialFiles.Add(longName);
                        }
                    }
                }
            }
            else
            {
                Tracer.Warning("{0} has no version resource", _path);
            }

            return specialFiles;
        }

        /// <summary>
        /// Creates a PE file symbol store key identity key.
        /// </summary>
        /// <param name="path">file name and path</param>
        /// <param name="timestamp">time stamp of pe image</param>
        /// <param name="sizeOfImage">size of pe image</param>
        /// <returns>symbol store keys (or empty enumeration)</returns>
        public static SymbolStoreKey GetKey(string path, uint timestamp, uint sizeOfImage)
        {
            Debug.Assert(path != null);

            // The clr special file flag can not be based on the GetSpecialFiles() list because 
            // that is only valid when "path" is the coreclr.dll.
            string fileName = GetFileName(path);
            bool clrSpecialFile = s_coreClrSpecialFiles.Contains(fileName) || 
                (s_longNameBinaryPrefixes.Any((prefix) => fileName.StartsWith(prefix)) && Path.GetExtension(fileName) == ".dll");

            string id = string.Format("{0:X8}{1:x}", timestamp, sizeOfImage);
            return BuildKey(path, id, clrSpecialFile);
        }
    }
}