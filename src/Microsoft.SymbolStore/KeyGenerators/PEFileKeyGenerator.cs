// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.FileFormats;
using Microsoft.FileFormats.PE;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class PEFileKeyGenerator : KeyGenerator
    {
        private const string CoreClrFileName = "coreclr.dll";
        private const string ClrFileName = "clr.dll";

        private const string SosFileName = "sos.dll";
        private const string CoreClrDACFileName = "mscordaccore.dll";
        private const string ClrDACFileName = "mscordacwks.dll";
        private const string DbiFileName = "mscordbi.dll";
        private static readonly string[] s_knownFilesWithLongNameVariant = new string[] { SosFileName, CoreClrDACFileName, ClrDACFileName };
        private static readonly string[] s_knownRuntimeSpecialFiles = new string[] { CoreClrDACFileName, ClrDACFileName, DbiFileName };

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
                if ((flags & KeyTypeFlags.RuntimeKeys) != 0 && (GetFileName(_path) == CoreClrFileName || GetFileName(_path) == ClrFileName))
                {
                    yield return GetKey(_path, _peFile.Timestamp, _peFile.SizeOfImage);
                }
                if ((flags & KeyTypeFlags.SymbolKey) != 0)
                {
                    PEPdbRecord[] pdbs = System.Array.Empty<PEPdbRecord>();
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
                    foreach (PEPerfMapRecord perfmapRecord in _peFile.PerfMapsV1)
                    {
                        if (perfmapRecord.Version > FileFormats.PerfMap.PerfMapFile.MaxKnownPerfMapVersion)
                        {
                            Tracer.Warning("Trying to get key for PerfmapFile {0} associated with PE {1} with version {2}, higher than max known version {3}",
                                perfmapRecord.Path, _path, perfmapRecord.Version, FileFormats.PerfMap.PerfMapFile.MaxKnownPerfMapVersion);
                        }
                        yield return PerfMapFileKeyGenerator.GetKey(perfmapRecord.Path, perfmapRecord.Signature, perfmapRecord.Version);
                    }
                }

                // Return keys for SOS modules for a given runtime module
                if ((flags & (KeyTypeFlags.ClrKeys)) != 0)
                {
                    string coreclrId = BuildId(_peFile.Timestamp, _peFile.SizeOfImage);
                    foreach (string specialFileName in GetSOSFiles(GetFileName(_path)))
                    {
                        yield return BuildKey(specialFileName, coreclrId);
                    }
                }

                // Return keys for DAC and DBI modules for a given runtime module
                if ((flags & (KeyTypeFlags.ClrKeys | KeyTypeFlags.DacDbiKeys)) != 0)
                {
                    string coreclrId = BuildId(_peFile.Timestamp, _peFile.SizeOfImage);
                    foreach (string specialFileName in GetDACFiles(GetFileName(_path)))
                    {
                        yield return BuildKey(specialFileName, coreclrId);
                    }
                }

                if ((flags & KeyTypeFlags.HostKeys) != 0)
                {
                    if ((_peFile.FileHeader.Characteristics & (ushort)ImageFile.Dll) == 0 && !_peFile.IsILImage)
                    {
                        string id = BuildId(_peFile.Timestamp, _peFile.SizeOfImage);

                        // The host program as itself (usually dotnet.exe)
                        yield return BuildKey(_path, id);

                        // apphost.exe downloaded as the host program name
                        yield return BuildKey(_path, prefix: null, id, "apphost.exe");
                    }
                }
            }
        }

        private IEnumerable<string> GetSOSFiles(string runtimeFileName)
        {
            if (runtimeFileName == ClrFileName)
            {
                return GetFilesLongNameVariants(SosFileName);
            }

            return Enumerable.Empty<string>();
        }

        private IEnumerable<string> GetDACFiles(string runtimeFileName)
        {
            if (runtimeFileName == CoreClrFileName)
            {
                string[] coreClrDACFiles = new string[] { CoreClrDACFileName, DbiFileName };
                IEnumerable<string> longNameDACFiles = GetFilesLongNameVariants(CoreClrDACFileName);
                return coreClrDACFiles.Concat(longNameDACFiles);
            }

            if (runtimeFileName == ClrFileName)
            {
                string[] clrDACFiles = new string[] { ClrDACFileName, DbiFileName };
                IEnumerable<string> longNameDACFiles = GetFilesLongNameVariants(ClrDACFileName);
                return clrDACFiles.Concat(longNameDACFiles);
            }

            return Enumerable.Empty<string>();
        }

        private IEnumerable<string> GetFilesLongNameVariants(string fileWithLongNameVariant)
        {
            if (!s_knownFilesWithLongNameVariant.Contains(fileWithLongNameVariant))
            {
                Tracer.Warning("{0} is not a recognized file with a long name variant", fileWithLongNameVariant);
                return Enumerable.Empty<string>();
            }

            VsFixedFileInfo fileVersionInfo = _peFile.VersionInfo;
            if (fileVersionInfo == null)
            {
                Tracer.Warning("{0} has no version resource, long name file keys could not be generated", _path);
                return Enumerable.Empty<string>();
            }

            string targetArchitecture;
            List<string> hostArchitectures = new();
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
                default:
                    Tracer.Warning("{0} has an architecture not used to generate long name file keys", _peFile);
                    return Enumerable.Empty<string>();
            }
            hostArchitectures.Add(targetArchitecture);

            string fileVersion = $"{fileVersionInfo.FileVersionMajor}.{fileVersionInfo.FileVersionMinor}.{fileVersionInfo.FileVersionBuild}.{fileVersionInfo.FileVersionRevision:00}";

            string buildFlavor = (fileVersionInfo.FileFlags & FileInfoFlags.Debug) == 0 ? "" :
                                 (fileVersionInfo.FileFlags & FileInfoFlags.SpecialBuild) != 0 ? ".dbg" : ".chk";

            List<string> longNameFileVariants = new();
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileWithLongNameVariant);
            foreach (string hostArchitecture in hostArchitectures)
            {
                longNameFileVariants.Add($"{fileNameWithoutExtension}_{hostArchitecture}_{targetArchitecture}_{fileVersion}{buildFlavor}.dll");
            }

            return longNameFileVariants;
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
            bool clrSpecialFile = s_knownRuntimeSpecialFiles.Contains(fileName) ||
                                  (s_knownFilesWithLongNameVariant.Any((file) => fileName.StartsWith(Path.GetFileNameWithoutExtension(file).ToLowerInvariant() + "_")) && Path.GetExtension(fileName) == ".dll");

            string id = BuildId(timestamp, sizeOfImage);
            return BuildKey(path, id, clrSpecialFile);
        }

        private static string BuildId(uint timestamp, uint sizeOfImage)
        {
            return string.Format("{0:X8}{1:x}", timestamp, sizeOfImage);
        }
    }
}
