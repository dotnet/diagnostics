// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostic.Tools.Symbol.Properties;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using Microsoft.SymbolStore.SymbolStores;

namespace Microsoft.Diagnostics.Tools.Symbol
{
    public class Program
    {
        private struct ServerInfo
        {
            public Uri Uri;
            public string PersonalAccessToken;
        }

        private readonly List<string> InputFilePaths = new();
        private readonly List<string> CacheDirectories = new();
        private readonly List<ServerInfo> SymbolServers = new();
        private string OutputDirectory;
        private TimeSpan? Timeout;
        private bool Overwrite;
        private bool Subdirectories;
        private bool Symbols;
        private bool Debugging;
        private bool Modules;
        private bool ForceWindowsPdbs;
        private bool HostOnly;
        private bool VerifyCore;
        private Tracer Tracer;

        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                goto usage;
            }
            Program program = new();
            Tracer tracer = new();
            program.Tracer = tracer;

            for (int i = 0; i < args.Length; i++)
            {
                string personalAccessToken = null;
                Uri uri;
                switch (args[i])
                {
                    case "--microsoft-symbol-server":
                        Uri.TryCreate("https://msdl.microsoft.com/download/symbols/", UriKind.Absolute, out uri);
                        program.SymbolServers.Add(new ServerInfo { Uri = uri, PersonalAccessToken = null });
                        break;

                    case "--authenticated-server-path":
                        if (++i < args.Length)
                        {
                            personalAccessToken = args[i];
                        }
                        else
                        {
                            goto usage;
                        }
                        if (string.IsNullOrEmpty(personalAccessToken))
                        {
                            tracer.Error("No personal access token option");
                            goto usage;
                        }
                        goto case "--server-path";

                    case "--server-path":
                        if (++i < args.Length)
                        {
                            // Make sure the server Uri ends with "/"
                            string serverPath = args[i].TrimEnd('/') + '/';
                            if (!Uri.TryCreate(serverPath, UriKind.Absolute, out uri) || uri.IsFile)
                            {
                                tracer.Error(Resources.InvalidServerPath, args[i]);
                                goto usage;
                            }
                            Uri.TryCreate(serverPath, UriKind.Absolute, out uri);
                            program.SymbolServers.Add(new ServerInfo { Uri = uri, PersonalAccessToken = personalAccessToken });
                        }
                        else
                        {
                            goto usage;
                        }
                        break;

                    case "-o":
                    case "--output":
                        if (++i < args.Length)
                        {
                            program.OutputDirectory = args[i];
                        }
                        else
                        {
                            goto usage;
                        }
                        break;

                    case "--overwrite":
                        program.Overwrite = true;
                        break;

                    case "--timeout":
                        if (++i < args.Length)
                        {
                            double timeoutInMinutes = double.Parse(args[i]);
                            program.Timeout = TimeSpan.FromMinutes(timeoutInMinutes);
                        }
                        else
                        {
                            goto usage;
                        }
                        break;

                    case "--cache-directory":
                        if (++i < args.Length)
                        {
                            program.CacheDirectories.Add(args[i]);
                        }
                        else
                        {
                            goto usage;
                        }
                        break;

                    case "--recurse-subdirectories":
                        program.Subdirectories = true;
                        break;

                    case "--modules":
                        program.Modules = true;
                        break;

                    case "--symbols":
                        program.Symbols = true;
                        break;

                    case "--debugging":
                        program.Debugging = true;
                        break;

                    case "--windows-pdbs":
                        program.ForceWindowsPdbs = true;
                        break;

                    case "--host-only":
                        program.HostOnly = true;
                        break;

                    case "--verifycore":
                        program.VerifyCore = true;
                        break;

                    case "-d":
                    case "--diagnostics":
                        tracer.Enabled = true;
                        tracer.EnabledVerbose = true;
                        break;

                    case "-h":
                    case "-?":
                    case "--help":
                        goto usage;

                    default:
                        string inputFile = args[i];
                        if (inputFile.StartsWith("-") || inputFile.StartsWith("--"))
                        {
                            tracer.Error(Resources.InvalidCommandLineOption, inputFile);
                            goto usage;
                        }
                        program.InputFilePaths.Add(inputFile);
                        break;
                }
            }
            if (program.VerifyCore)
            {
                program.VerifyCoreDump();
            }
            else
            {
                // Default to public Microsoft symbol server
                if (program.SymbolServers.Count == 0)
                {
                    Uri.TryCreate("https://msdl.microsoft.com/download/symbols/", UriKind.Absolute, out Uri uri);
                    program.SymbolServers.Add(new ServerInfo { Uri = uri, PersonalAccessToken = null });
                }
                foreach (ServerInfo server in program.SymbolServers)
                {
                    tracer.WriteLine(Resources.DownloadFromUri, server.Uri);
                }
                if (program.OutputDirectory != null)
                {
                    Directory.CreateDirectory(program.OutputDirectory);
                    tracer.WriteLine(Resources.WritingFilesToOutput, program.OutputDirectory);
                }
                try
                {
                    program.DownloadFiles().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    tracer.Error("{0}{1}", ex.Message, ex.InnerException != null ? " -> " + ex.InnerException.Message : "");
                }
            }
            return;

        usage:
            PrintUsage();
        }

        private static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine(Resources.UsageOptions);
        }

        internal async Task DownloadFiles()
        {
            using (Microsoft.SymbolStore.SymbolStores.SymbolStore symbolStore = BuildSymbolStore())
            {
                foreach (SymbolStoreKeyWrapper wrapper in GetKeys().Distinct())
                {
                    SymbolStoreKey key = wrapper.Key;
                    if (symbolStore != null)
                    {
                        using (SymbolStoreFile symbolFile = await symbolStore.GetFile(key, CancellationToken.None).ConfigureAwait(false))
                        {
                            if (symbolFile != null)
                            {
                                await WriteFile(symbolFile, wrapper).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
        }

        private Microsoft.SymbolStore.SymbolStores.SymbolStore BuildSymbolStore()
        {
            Microsoft.SymbolStore.SymbolStores.SymbolStore store = null;

            foreach (ServerInfo server in ((IEnumerable<ServerInfo>)SymbolServers).Reverse())
            {
                store = new HttpSymbolStore(Tracer, store, server.Uri, server.PersonalAccessToken);
                if (Timeout.HasValue && store is HttpSymbolStore http)
                {
                    http.Timeout = Timeout.Value;
                }
            }

            // Add default symbol cache if one wasn't set by the command line
            if (CacheDirectories.Count == 0)
            {
                CacheDirectories.Add(GetDefaultSymbolCache());
            }

            foreach (string cache in ((IEnumerable<string>)CacheDirectories).Reverse())
            {
                store = new CacheSymbolStore(Tracer, store, cache);
            }

            return store;
        }

        private sealed class SymbolStoreKeyWrapper
        {
            public readonly SymbolStoreKey Key;
            public readonly string InputFile;

            internal SymbolStoreKeyWrapper(SymbolStoreKey key, string inputFile)
            {
                Key = key;
                InputFile = inputFile;
            }

            /// <summary>
            /// Returns the hash of the index.
            /// </summary>
            public override int GetHashCode()
            {
                return Key.GetHashCode();
            }

            /// <summary>
            /// Only the index is compared or hashed. The FileName is already
            /// part of the index.
            /// </summary>
            public override bool Equals(object obj)
            {
                SymbolStoreKeyWrapper wrapper = (SymbolStoreKeyWrapper)obj;
                return Key.Equals(wrapper.Key);
            }
        }

        private IEnumerable<SymbolStoreKeyWrapper> GetKeys()
        {
            IEnumerable<string> inputFiles = GetInputFiles();

            foreach (string inputFile in inputFiles)
            {
                foreach (KeyGenerator generator in GetKeyGenerators(inputFile))
                {
                    KeyTypeFlags flags = KeyTypeFlags.None;
                    if (HostOnly)
                    {
                        flags |= KeyTypeFlags.HostKeys;
                    }
                    if (Symbols)
                    {
                        flags |= KeyTypeFlags.SymbolKey | KeyTypeFlags.PerfMapKeys;
                    }
                    if (Modules)
                    {
                        flags |= KeyTypeFlags.IdentityKey;
                    }
                    if (Debugging)
                    {
                        flags |= KeyTypeFlags.RuntimeKeys | KeyTypeFlags.ClrKeys;
                    }
                    if (flags == KeyTypeFlags.None)
                    {
                        if (generator.IsDump())
                        {
                            // The default for dumps is to download everything
                            flags = KeyTypeFlags.IdentityKey | KeyTypeFlags.SymbolKey | KeyTypeFlags.ClrKeys | KeyTypeFlags.HostKeys;
                        }
                        else
                        {
                            // Otherwise the default is just the symbol files
                            flags = KeyTypeFlags.SymbolKey | KeyTypeFlags.PerfMapKeys;
                        }
                    }
                    if (ForceWindowsPdbs)
                    {
                        flags |= KeyTypeFlags.ForceWindowsPdbs;
                    }
                    foreach (SymbolStoreKeyWrapper wrapper in generator.GetKeys(flags).Select((key) => new SymbolStoreKeyWrapper(key, inputFile)))
                    {
                        yield return wrapper;
                    }
                }
            }
        }

        private IEnumerable<KeyGenerator> GetKeyGenerators(string inputFile)
        {
            using (Stream inputStream = File.Open(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                SymbolStoreFile file = new(inputStream, inputFile);
                string extension = Path.GetExtension(inputFile);
                yield return new FileKeyGenerator(Tracer, file);
            }
        }

        private async Task WriteFile(SymbolStoreFile file, SymbolStoreKeyWrapper wrapper)
        {
            if (OutputDirectory != null)
            {
                await WriteFileToDirectory(file.Stream, wrapper.Key.FullPathName, OutputDirectory).ConfigureAwait(false);
            }
            else
            {
                await WriteFileToDirectory(file.Stream, wrapper.Key.FullPathName, Path.GetDirectoryName(wrapper.InputFile)).ConfigureAwait(false);
            }
        }

        private async Task WriteFileToDirectory(Stream stream, string fileName, string destinationDirectory)
        {
            stream.Position = 0;
            string destination = Path.Combine(destinationDirectory, Path.GetFileName(fileName.Replace('\\', '/')));
            if (!Overwrite && File.Exists(destination))
            {
                Tracer.WriteLine(Resources.FileAlreadyExists, destination);
            }
            else
            {
                Tracer.WriteLine(Resources.WritingFile, destination);
                using (Stream destinationStream = File.OpenWrite(destination))
                {
                    await stream.CopyToAsync(destinationStream).ConfigureAwait(false);
                }
            }
        }

        private static string GetDefaultSymbolCache()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(Path.GetTempPath(), "SymbolCache");
            }
            else
            {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".dotnet", "symbolcache");
            }
        }

        internal void VerifyCoreDump()
        {
            foreach (string inputFile in GetInputFiles())
            {
                Console.WriteLine($"{inputFile}");

                using Stream inputStream = File.Open(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                StreamAddressSpace dataSource = new(inputStream);
                ELFCoreFile core = new(dataSource);

                if (Tracer.Enabled)
                {
                    foreach (ELFProgramSegment segment in core.Segments)
                    {
                        Tracer.Information("{0:X16}-{1:X16} {2:X8} {3:X8} {4}",
                            segment.Header.VirtualAddress.Value,
                            segment.Header.VirtualAddress + segment.Header.VirtualSize,
                            segment.Header.FileOffset.Value,
                            (ulong)segment.Header.FileSize,
                            segment.Header.Type);
                    }
                }

                foreach (ELFLoadedImage image in core.LoadedImages)
                {
                    Console.WriteLine("{0:X16} {1}", image.LoadAddress, image.Path);
                    Exception elfException = null;
                    Exception machoException = null;
                    Exception peException = null;
                    try
                    {
                        ELFFile elfFile = image.Image;
                        if (elfFile.IsValid())
                        {
                            try
                            {
                                byte[] buildid = elfFile.BuildID;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("                 ELF file invalid build id - {0}", ex.Message);
                            }
                            foreach (ELFProgramSegment segment in elfFile.Segments)
                            {
                                Tracer.Verbose("                 {0:X16}-{1:X16} file off {2:X8} file size {3:X8} {4}",
                                    segment.Header.VirtualAddress.Value,
                                    segment.Header.VirtualAddress + segment.Header.VirtualSize,
                                    segment.Header.FileOffset.Value,
                                    (ulong)segment.Header.FileSize,
                                    segment.Header.Type);

                                if (segment.Header.Type == ELFProgramHeaderType.Note ||
                                    segment.Header.Type == ELFProgramHeaderType.Dynamic ||
                                    segment.Header.Type == ELFProgramHeaderType.GnuEHFrame)
                                {
                                    try
                                    {
                                        byte[] data = segment.Contents.Read(0, (uint)segment.Header.VirtualSize);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("                 ELF file segment {0} virt addr {1:X16} virt size {2:X8} INVALID - {3}",
                                            segment.Header.Type, segment.Header.VirtualAddress, segment.Header.VirtualSize, ex.Message);
                                    }
                                }
                            }

                            // The ELF module was valid try next module
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        elfException = ex;
                    }

                    IAddressSpace addressSpace = new RelativeAddressSpace(core.DataSource, image.LoadAddress, core.DataSource.Length);
                    try
                    {
                        MachOFile machoFile = new(addressSpace);
                        if (machoFile.IsValid())
                        {
                            try
                            {
                                byte[] uuid = machoFile.Uuid;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("                 MachO file invalid uuid - {0}", ex.Message);
                            }
                            foreach (MachSegment segment in machoFile.Segments)
                            {
                                Tracer.Verbose("                 {0:X16}-{1:X16} offset {2:X16} size {3:X16} {4} {5}",
                                    (ulong)segment.LoadCommand.VMAddress,
                                    segment.LoadCommand.VMAddress + segment.LoadCommand.VMSize,
                                    (ulong)segment.LoadCommand.FileOffset,
                                    (ulong)segment.LoadCommand.FileSize,
                                    segment.LoadCommand.Command,
                                    segment.LoadCommand.SegName);

                                foreach (MachSection section in segment.Sections)
                                {
                                    Tracer.Verbose("                         addr {0:X16} size {1:X16} offset {2:X8} {3}",
                                        (ulong)section.Address,
                                        (ulong)section.Size,
                                        section.Offset,
                                        section.SectionName);
                                }
                            }

                            // The MachO module was valid try next module
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        machoException = ex;
                    }

                    try
                    {
                        PEFile peFile = new(addressSpace, true);
                        if (peFile.IsValid())
                        {
                            // The PE module was valid try next module
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        peException = ex;
                    }

                    Console.WriteLine("{0:X16} invalid image - {1}", image.LoadAddress, image.Path);
                    if (elfException != null)
                    {
                        Tracer.Verbose("ELF {0}", elfException.Message);
                    }
                    if (machoException != null)
                    {
                        Tracer.Verbose("MachO {0}", machoException.Message);
                    }
                    if (peException != null)
                    {
                        Tracer.Verbose("PE {0}", peException.Message);
                    }
                }

                ulong segmentsTotal = core.Segments.Max(s => s.Header.FileOffset + s.Header.FileSize);
                if (segmentsTotal > dataSource.Length)
                {
                    Console.WriteLine($"ERROR: Core file not complete: file size 0x{dataSource.Length:X8} segments total 0x{segmentsTotal:X8}");
                }
            }
        }

        private IEnumerable<string> GetInputFiles()
        {
            IEnumerable<string> inputFiles = InputFilePaths.SelectMany((string file) =>
            {
                string directory = Path.GetDirectoryName(file);
                string pattern = Path.GetFileName(file);
                return Directory.EnumerateFiles(string.IsNullOrWhiteSpace(directory) ? "." : directory, pattern,
                    Subdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            });

            if (!inputFiles.Any())
            {
                throw new ArgumentException("Input files not found");
            }
            return inputFiles;
        }
    }
}
