// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.PE;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using Microsoft.SymbolStore.SymbolStores;
using SOS;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Symbol services to configure symbol servers, caches and directory search
    /// </summary>
    public class SymbolService : ISymbolService
    {
        /// <summary>
        /// Symbol server URLs
        /// </summary>
        public const string MsdlSymbolServer = "https://msdl.microsoft.com/download/symbols/";
        public const string SymwebSymbolServer = "https://symweb.azurefd.net/";

        private static string _symwebHost = new Uri(SymwebSymbolServer).Host;

        private readonly IHost _host;
        private string _defaultSymbolCache;
        private SymbolStore.SymbolStores.SymbolStore _symbolStore;

        public SymbolService(IHost host)
        {
            _host = host;
            OnChangeEvent = new ServiceEvent();
            // dbgeng's console can not handle the async logging (Tracer output on another thread than the main one).
            Tracer.Enable = host.HostType != HostType.DbgEng;
        }

        #region ISymbolService

        /// <summary>
        /// Invoked when anything changes in the symbol service (adding servers, caches, or directories, clearing store, etc.)
        /// </summary>
        public IServiceEvent OnChangeEvent { get; }

        /// <summary>
        /// Returns true if symbol download has been enabled.
        /// </summary>
        public bool IsSymbolStoreEnabled => _symbolStore != null;

        /// <summary>
        /// The default symbol server URL (normally msdl) when not overridden in AddSymbolServer.
        /// </summary>
        public string DefaultSymbolPath { get; set; } = MsdlSymbolServer;

        /// <summary>
        /// The default symbol cache path:
        /// * dbgeng on Windows uses the dbgeng symbol cache path: %PROGRAMDATA%\dbg\sym
        /// * VS or dotnet-dump on Windows use the VS symbol cache path: %TEMPDIR%\SymbolCache
        /// * dotnet-dump/lldb on Linux/MacOS uses: $HOME/.dotnet/symbolcache
        /// </summary>
        public string DefaultSymbolCache
        {
            get
            {
                if (_defaultSymbolCache == null)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _defaultSymbolCache = _host.HostType switch
                        {
                            HostType.DbgEng => Path.Combine(Environment.GetEnvironmentVariable("PROGRAMDATA"), "dbg", "sym"),
                            _ => Path.Combine(Path.GetTempPath(), "SymbolCache"),
                        };
                    }
                    else
                    {
                        _defaultSymbolCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "symbolcache");
                    }
                }
                return _defaultSymbolCache;
            }
            set
            {
                _defaultSymbolCache = value;
            }
        }

        /// <summary>
        /// The time out in minutes passed to the HTTP symbol store when not overridden in AddSymbolServer.
        /// </summary>
        public int DefaultTimeout { get; set; } = 4;

        /// <summary>
        /// The retry count passed to the HTTP symbol store when not overridden in AddSymbolServer.
        /// </summary>
        public int DefaultRetryCount { get; set; }

        /// <summary>
        /// Reset any HTTP symbol stores marked with a client failure
        /// </summary>
        public void Reset() => ForEachSymbolStore<HttpSymbolStore>((httpSymbolStore) => httpSymbolStore.ResetClientFailure());

        /// <summary>
        /// Parses the Windows debugger symbol path (srv*, cache*, etc.).
        /// </summary>
        /// <param name="symbolPath">Windows symbol path</param>
        /// <returns>if false, error parsing symbol path</returns>
        public bool ParseSymbolPath(string symbolPath)
        {
            string[] paths = symbolPath.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string path in paths.Reverse())
            {
                string[] parts = path.Split(new char[] { '*' }, StringSplitOptions.None);
                if (parts.Length > 0)
                {
                    List<string> symbolCachePaths = new();
                    string symbolDirectoryPath = null;
                    string symbolServerPath = null;

                    void ParseServer(int start)
                    {
                        symbolServerPath = MsdlSymbolServer;
                        for (int i = start; i < parts.Length; i++)
                        {
                            if (string.IsNullOrEmpty(parts[i]))
                            {
                                // srv** means use default cache
                                if (i != (parts.Length - 1))
                                {
                                    symbolCachePaths.Add(DefaultSymbolCache);
                                }
                            }
                            else if (i < (parts.Length - 1))
                            {
                                symbolCachePaths.Add(parts[i]);
                            }
                            else
                            {
                                symbolServerPath = parts[i];
                            }
                        }
                    }

                    switch (parts[0].ToLowerInvariant())
                    {
                        case "symsrv":
                            if (parts.Length <= 2)
                            {
                                return false;
                            }
                            // ignore symsrv.dll or other server dlls in parts[2]
                            ParseServer(2);
                            break;

                        case "srv":
                            if (parts.Length <= 1)
                            {
                                return false;
                            }
                            ParseServer(1);
                            break;

                        case "cache":
                            if (parts.Length <= 1)
                            {
                                return false;
                            }
                            else
                            {
                                for (int i = 1; i < parts.Length; i++)
                                {
                                    if (string.IsNullOrEmpty(parts[i]))
                                    {
                                        if (i == 1)
                                        {
                                            symbolCachePaths.Add(DefaultSymbolCache);
                                        }
                                    }
                                    else
                                    {
                                        symbolCachePaths.Add(parts[i]);
                                    }
                                }
                            }
                            break;

                        default:
                            // Directory path search
                            if (parts.Length != 1)
                            {
                                return false;
                            }
                            symbolDirectoryPath = parts[0];
                            break;
                    }
                    if (symbolServerPath != null)
                    {
                        symbolServerPath = symbolServerPath.Trim();
                        if (IsSymweb(symbolServerPath))
                        {
                            if (!AddSymwebSymbolServer(includeInteractiveCredentials: false))
                            {
                                return false;
                            }
                        }
                        else
                        {
                            if (!AddSymbolServer(symbolServerPath))
                            {
                                return false;
                            }
                        }
                    }
                    foreach (string symbolCachePath in symbolCachePaths.Reverse<string>())
                    {
                        AddCachePath(symbolCachePath.Trim());
                    }
                    if (symbolDirectoryPath != null)
                    {
                        AddDirectoryPath(symbolDirectoryPath.Trim());
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Add the cloud symweb symbol server with authentication.
        /// </summary>
        /// <param name="includeInteractiveCredentials">specifies whether credentials requiring user interaction will be included in the default authentication flow</param>
        /// <param name="timeoutInMinutes">symbol server timeout in minutes (optional uses <see cref="DefaultTimeout"/> if null)</param>
        /// <param name="retryCount">number of retries (optional uses <see cref="DefaultRetryCount"/> if null)</param>
        /// <returns>if false, failure</returns>
        public bool AddSymwebSymbolServer(
            bool includeInteractiveCredentials = false,
            int? timeoutInMinutes = null,
            int? retryCount = null)
        {
            TokenCredential tokenCredential = new DefaultAzureCredential(includeInteractiveCredentials);
            AccessToken accessToken = default;
            async ValueTask<AuthenticationHeaderValue> authenticationFunc(CancellationToken token)
            {
                try
                {
                    if (accessToken.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(2))
                    {
                        accessToken = await tokenCredential.GetTokenAsync(new TokenRequestContext(["api://af9e1c69-e5e9-4331-8cc5-cdf93d57bafa/.default"]), token).ConfigureAwait(false);
                    }
                    return new AuthenticationHeaderValue("Bearer", accessToken.Token);
                }
                catch (Exception ex) when (ex is CredentialUnavailableException or AuthenticationFailedException)
                {
                    Trace.TraceError($"AddSymwebSymbolServer: {ex}");
                    return null;
                }
            }
            return AddSymbolServer(SymwebSymbolServer, timeoutInMinutes, retryCount, authenticationFunc);
        }

        /// <summary>
        /// Add symbol server to search path. The server URL can be the cloud symweb.
        /// </summary>
        /// <param name="accessToken">PAT or access token</param>
        /// <param name="symbolServerPath">symbol server url (optional, uses <see cref="DefaultSymbolPath"/> if null)</param>
        /// <param name="timeoutInMinutes">symbol server timeout in minutes (optional uses <see cref="DefaultTimeout"/> if null)</param>
        /// <param name="retryCount">number of retries (optional uses <see cref="DefaultRetryCount"/> if null)</param>
        /// <returns>if false, failure</returns>
        public bool AddAuthenticatedSymbolServer(
            string accessToken,
            string symbolServerPath = null,
            int? timeoutInMinutes = null,
            int? retryCount = null)
        {
            if (accessToken == null)
            {
                throw new ArgumentNullException(nameof(accessToken));
            }
            AuthenticationHeaderValue authenticationValue = new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{accessToken}")));
            return AddSymbolServer(symbolServerPath, timeoutInMinutes, retryCount, (_) => new ValueTask<AuthenticationHeaderValue>(authenticationValue));
        }

        /// <summary>
        /// Add symbol server to search path.
        /// </summary>
        /// <param name="symbolServerPath">symbol server url (optional, uses <see cref="DefaultSymbolPath"/> if null)</param>
        /// <param name="timeoutInMinutes">symbol server timeout in minutes (optional uses <see cref="DefaultTimeout"/> if null)</param>
        /// <param name="retryCount">number of retries (optional uses <see cref="DefaultRetryCount"/> if null)</param>
        /// <returns>if false, failure</returns>
        public bool AddSymbolServer(
            string symbolServerPath = null,
            int? timeoutInMinutes = null,
            int? retryCount = null)
        {
            return AddSymbolServer(symbolServerPath, timeoutInMinutes, retryCount, authenticationFunc: null);
        }

        /// <summary>
        /// Add symbol server to search path.
        /// </summary>
        /// <param name="symbolServerPath">symbol server url (optional, uses <see cref="DefaultSymbolPath"/> if null)</param>
        /// <param name="timeoutInMinutes">symbol server timeout in minutes (optional uses <see cref="DefaultTimeout"/> if null)</param>
        /// <param name="retryCount">number of retries (optional uses <see cref="DefaultRetryCount"/> if null)</param>
        /// <param name="authenticationFunc">function that returns the authentication value for a request</param>
        /// <returns>if false, failure</returns>
        public bool AddSymbolServer(
            string symbolServerPath,
            int? timeoutInMinutes,
            int? retryCount,
            Func<CancellationToken, ValueTask<AuthenticationHeaderValue>> authenticationFunc)
        {
            // Add symbol server URL if exists
            symbolServerPath ??= DefaultSymbolPath;

            // Validate symbol server path
            if (!Uri.TryCreate(symbolServerPath.TrimEnd('/') + '/', UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            // Add a cache symbol store if file or UNC path
            if (uri.IsFile || uri.IsUnc)
            {
                AddCachePath(symbolServerPath);
            }
            else
            {
                SymbolStore.SymbolStores.SymbolStore store = _symbolStore;
                if (!IsDuplicateSymbolStore<HttpSymbolStore>(store, (httpSymbolStore) => uri.Equals(httpSymbolStore.Uri)))
                {
                    // Create http symbol server store
                    HttpSymbolStore httpSymbolStore = new(Tracer.Instance, store, uri, authenticationFunc)
                    {
                        Timeout = TimeSpan.FromMinutes(timeoutInMinutes.GetValueOrDefault(DefaultTimeout)),
                        RetryCount = retryCount.GetValueOrDefault(DefaultRetryCount)
                    };
                    SetSymbolStore(httpSymbolStore);
                }
            }

            return true;
        }

        /// <summary>
        /// Add cache path to symbol search path
        /// </summary>
        /// <param name="symbolCachePath">symbol cache directory path (optional)</param>
        public void AddCachePath(string symbolCachePath)
        {
            if (symbolCachePath == null)
            {
                throw new ArgumentNullException(nameof(symbolCachePath));
            }

            SymbolStore.SymbolStores.SymbolStore store = _symbolStore;
            symbolCachePath = Path.GetFullPath(symbolCachePath);

            // Check only the first symbol store for duplication. The same cache directory can be
            // added more than once but just not more than once in a row.
            if (!(store is CacheSymbolStore cacheSymbolStore && IsPathEqual(symbolCachePath, cacheSymbolStore.CacheDirectory)))
            {
                SetSymbolStore(new CacheSymbolStore(Tracer.Instance, store, symbolCachePath));
            }
        }

        /// <summary>
        /// Add directory path to symbol search path
        /// </summary>
        /// <param name="symbolDirectoryPath">symbol directory path to search (optional)</param>
        public void AddDirectoryPath(string symbolDirectoryPath)
        {
            if (symbolDirectoryPath == null)
            {
                throw new ArgumentNullException(nameof(symbolDirectoryPath));
            }

            SymbolStore.SymbolStores.SymbolStore store = _symbolStore;
            symbolDirectoryPath = Path.GetFullPath(symbolDirectoryPath);

            if (!IsDuplicateSymbolStore<DirectorySymbolStore>(store, (directorySymbolStore) => IsPathEqual(symbolDirectoryPath, directorySymbolStore.Directory)))
            {
                SetSymbolStore(new DirectorySymbolStore(Tracer.Instance, store, symbolDirectoryPath));
            }
        }

        /// <summary>
        /// This function disables any symbol downloading support.
        /// </summary>
        public void DisableSymbolStore()
        {
            SetSymbolStore(null);
        }

        /// <summary>
        /// Downloads module file
        /// </summary>
        /// <param name="module">module interface</param>
        /// <returns>module path or null</returns>
        public string DownloadModuleFile(IModule module)
        {
            string downloadFilePath = DownloadPE(module, KeyTypeFlags.IdentityKey);
            if (downloadFilePath is null)
            {
                if (module.Target.OperatingSystem == OSPlatform.Linux)
                {
                    downloadFilePath = DownloadELF(module, KeyTypeFlags.IdentityKey);
                }
                else if (module.Target.OperatingSystem == OSPlatform.OSX)
                {
                    downloadFilePath = DownloadMachO(module, KeyTypeFlags.IdentityKey);
                }
            }
            return downloadFilePath;
        }

        /// <summary>
        /// Downloads the symbol file for module
        /// </summary>
        /// <param name="module">module interface</param>
        /// <returns>module path or null</returns>
        public string DownloadSymbolFile(IModule module)
        {
            string downloadFilePath = DownloadPE(module, KeyTypeFlags.SymbolKey);
            if (downloadFilePath is null)
            {
                if (module.Target.OperatingSystem == OSPlatform.Linux)
                {
                    downloadFilePath = DownloadELF(module, KeyTypeFlags.SymbolKey);
                }
                else if (module.Target.OperatingSystem == OSPlatform.OSX)
                {
                    downloadFilePath = DownloadMachO(module, KeyTypeFlags.SymbolKey);
                }
            }
            return downloadFilePath;
        }

        /// <summary>
        /// Download a file from the symbol stores/server.
        /// </summary>
        /// <param name="index">index to lookup on symbol server</param>
        /// <param name="file">the full path name of the file</param>
        public string DownloadFile(string index, string file) => DownloadFile(new SymbolStoreKey(index, file));

        /// <summary>
        /// Returns the metadata for the assembly
        /// </summary>
        /// <param name="imagePath">file name and path to module</param>
        /// <param name="imageTimestamp">module timestamp</param>
        /// <param name="imageSize">size of PE image</param>
        /// <returns>metadata</returns>
        public ImmutableArray<byte> GetMetadata(string imagePath, uint imageTimestamp, uint imageSize)
        {
            try
            {
                Stream peStream = null;
                if (imagePath != null && File.Exists(imagePath))
                {
                    peStream = Utilities.TryOpenFile(imagePath);
                }
                else if (IsSymbolStoreEnabled)
                {
                    SymbolStoreKey key = PEFileKeyGenerator.GetKey(imagePath, imageTimestamp, imageSize);
                    peStream = GetSymbolStoreFile(key)?.Stream;
                }
                if (peStream != null)
                {
                    using PEReader peReader = new(peStream, PEStreamOptions.Default);
                    if (peReader.HasMetadata)
                    {
                        PEMemoryBlock metadataInfo = peReader.GetMetadata();
                        return metadataInfo.GetContent();
                    }
                }
            }
            catch (Exception ex) when
                (ex is UnauthorizedAccessException or
                 BadImageFormatException or
                 InvalidVirtualAddressException or
                 IOException)
            {
                Trace.TraceError($"GetMetaData: {ex.Message}");
            }
            return ImmutableArray<byte>.Empty;
        }

        /// <summary>
        /// Returns the portable PDB reader for the assembly path
        /// </summary>
        /// <param name="assemblyPath">file path of the assembly or null if the module is in-memory or dynamic</param>
        /// <param name="isFileLayout">type of in-memory PE layout, if true, file based layout otherwise, loaded layout</param>
        /// <param name="peStream">in-memory PE stream or null</param>
        /// <returns>symbol file or null</returns>
        /// <remarks>
        /// Assumes that neither PE image nor PDB loaded into memory can be unloaded or moved around.
        /// </remarks>
        public ISymbolFile OpenSymbolFile(string assemblyPath, bool isFileLayout, Stream peStream)
        {
            if (assemblyPath is null)
            {
                throw new ArgumentNullException(nameof(assemblyPath));
            }

            if (peStream is null)
            {
                throw new ArgumentNullException(nameof(peStream));
            }

            if (!peStream.CanSeek)
            {
                throw new ArgumentException("Stream is not seakable", nameof(peStream));
            }

            PEStreamOptions options = isFileLayout ? PEStreamOptions.Default : PEStreamOptions.IsLoadedImage;
            if (peStream == null)
            {
                peStream = Utilities.TryOpenFile(assemblyPath);
                if (peStream == null)
                {
                    return null;
                }

                options = PEStreamOptions.Default;
            }

            try
            {
                using (PEReader peReader = new(peStream, options))
                {
                    ReadPortableDebugTableEntries(peReader, out DebugDirectoryEntry codeViewEntry, out DebugDirectoryEntry embeddedPdbEntry);

                    // First try .pdb file specified in CodeView data (we prefer .pdb file on disk over embedded PDB
                    // since embedded PDB needs decompression which is less efficient than memory-mapping the file).
                    if (codeViewEntry.DataSize != 0)
                    {
                        SymbolFile result = TryOpenReaderFromCodeView(peReader, codeViewEntry, assemblyPath);
                        if (result != null)
                        {
                            return result;
                        }
                    }

                    // if it failed try Embedded Portable PDB (if available):
                    if (embeddedPdbEntry.DataSize != 0)
                    {
                        return TryOpenReaderFromEmbeddedPdb(peReader, embeddedPdbEntry);
                    }
                }
            }
            catch (Exception e) when (e is BadImageFormatException or IOException)
            {
                // nop
            }

            return null;
        }

        /// <summary>
        /// Returns the portable PDB reader for the portable PDB stream
        /// </summary>
        /// <param name="pdbStream">portable PDB memory or file stream</param>
        /// <returns>symbol file or null</returns>
        /// <remarks>
        /// Assumes that the PDB loaded into memory can be unloaded or moved around.
        /// </remarks>
        public ISymbolFile OpenSymbolFile(Stream pdbStream)
        {
            if (pdbStream is null)
            {
                throw new ArgumentNullException(nameof(pdbStream));
            }

            if (!pdbStream.CanSeek)
            {
                throw new ArgumentException("Stream is not seakable", nameof(pdbStream));
            }

            byte[] buffer = new byte[sizeof(uint)];
            pdbStream.Position = 0;
            if (pdbStream.Read(buffer, 0, sizeof(uint)) != sizeof(uint))
            {
                return null;
            }
            uint signature = BitConverter.ToUInt32(buffer, 0);

            // quick check to avoid throwing exceptions below in common cases:
            const uint ManagedMetadataSignature = 0x424A5342;
            if (signature != ManagedMetadataSignature)
            {
                // not a Portable PDB
                return null;
            }

            SymbolFile result = null;
            MetadataReaderProvider provider = null;
            try
            {
                pdbStream.Position = 0;
                provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
                result = new SymbolFile(provider, provider.GetMetadataReader());
            }
            catch (Exception e) when (e is BadImageFormatException or IOException)
            {
                return null;
            }
            finally
            {
                if (result == null)
                {
                    provider?.Dispose();
                }
            }

            return result;
        }

        #endregion

        /// <summary>
        /// Finds or downloads the PE module
        /// </summary>
        /// <param name="module">module instance</param>
        /// <param name="flags"></param>
        /// <returns>module path or null</returns>
        private string DownloadPE(IModule module, KeyTypeFlags flags)
        {
            SymbolStoreKey fileKey = null;
            SymbolStoreKey tempFileKey = null;
            string fileName;
            if ((flags & KeyTypeFlags.IdentityKey) != 0)
            {
                if (!module.IndexTimeStamp.HasValue || !module.IndexFileSize.HasValue)
                {
                    return null;
                }
                fileName = module.FileName;
                fileKey = PEFileKeyGenerator.GetKey(Path.GetFileName(fileName), module.IndexTimeStamp.Value, module.IndexFileSize.Value);
                if (fileKey is null)
                {
                    Trace.TraceWarning($"DownLoadPE: no key generated for module {fileName} ");
                    return null;
                }
            }
            else if ((flags & KeyTypeFlags.SymbolKey) != 0)
            {
                IEnumerable<PdbFileInfo> pdbInfos = module.GetPdbFileInfos();
                foreach (PdbFileInfo pdbInfo in pdbInfos)
                {
                    if (pdbInfo.IsPortable)
                    {
                        fileKey = PortablePDBFileKeyGenerator.GetKey(pdbInfo.Path, pdbInfo.Guid);
                        if (fileKey is not null)
                        {
                            break;
                        }
                    }
                    else
                    {
                        tempFileKey ??= PDBFileKeyGenerator.GetKey(pdbInfo.Path, pdbInfo.Guid, pdbInfo.Revision);
                    }
                }

                fileKey ??= tempFileKey;
                if (fileKey is not null)
                {
                    fileName = fileKey.FullPathName;
                }
                else
                {
                    Trace.TraceWarning($"DownLoadPE: no key generated for module PDB {module.FileName} ");
                    return null;
                }
            }
            else
            {
                throw new ArgumentException($"Key flag not supported {flags}");
            }

            // Check if the file is local and the key matches the module
            if (File.Exists(fileName))
            {
                using Stream stream = Utilities.TryOpenFile(fileName);
                if (stream is not null)
                {
                    if ((flags & KeyTypeFlags.IdentityKey) != 0)
                    {
                        PEFile peFile = new(new StreamAddressSpace(stream), false);
                        PEFileKeyGenerator generator = new(Tracer.Instance, peFile, fileName);
                        foreach (SymbolStoreKey key in generator.GetKeys(flags).ToArray())
                        {
                            if (fileKey.Equals(key))
                            {
                                Trace.TraceInformation($"DownloadPE: local file match {fileName}");
                                return fileName;
                            }
                        }
                    }
                    else if ((flags & KeyTypeFlags.SymbolKey) != 0)
                    {
                        KeyGenerator generator = new PortablePDBFileKeyGenerator(Tracer.Instance, new SymbolStoreFile(stream, fileName));
                        foreach (SymbolStoreKey key in generator.GetKeys(KeyTypeFlags.IdentityKey))
                        {
                            if (fileKey.Equals(key))
                            {
                                Trace.TraceInformation($"DownloadPE: local file match {fileName}");
                                return fileName;
                            }
                        }

                        generator = new PDBFileKeyGenerator(Tracer.Instance, new SymbolStoreFile(stream, fileName));
                        foreach (SymbolStoreKey key in generator.GetKeys(KeyTypeFlags.IdentityKey))
                        {
                            if (fileKey.Equals(key))
                            {
                                Trace.TraceInformation($"DownloadPE: local file match {fileName}");
                                return fileName;
                            }
                        }
                    }
                }
            }

            // Now download the module from the symbol server if local file doesn't exists or doesn't have the right key
            string downloadFilePath = DownloadFile(fileKey);
            if (!string.IsNullOrEmpty(downloadFilePath))
            {
                Trace.TraceInformation("DownloadPE: downloaded {0}", downloadFilePath);
                return downloadFilePath;
            }

            return null;
        }

        /// <summary>
        /// Finds or downloads the ELF module
        /// </summary>
        /// <param name="module">module instance</param>
        /// <param name="flags"></param>
        /// <returns>module path or null</returns>
        private string DownloadELF(IModule module, KeyTypeFlags flags)
        {
            if ((flags & (KeyTypeFlags.IdentityKey | KeyTypeFlags.SymbolKey)) == 0)
            {
                throw new ArgumentException($"Key flag not supported {flags}");
            }

            if (module.BuildId.IsDefaultOrEmpty)
            {
                Trace.TraceWarning($"DownloadELF: module {module.FileName} has no build id");
                return null;
            }

            string symbolFileName = (flags & KeyTypeFlags.SymbolKey) != 0 ? module.GetSymbolFileName() : null;
            SymbolStoreKey fileKey = ELFFileKeyGenerator.GetKeys(flags, module.FileName, module.BuildId.ToArray(), symbolFile: false, symbolFileName).SingleOrDefault();
            if (fileKey is null)
            {
                Trace.TraceWarning($"DownloadELF: no index generated for module {module.FileName} ");
                return null;
            }

            // Check if the file is local and the key matches the module
            string fileName = fileKey.FullPathName;
            if (File.Exists(fileName))
            {
                using ELFFile elfFile = Utilities.OpenELFFile(fileName);
                if (elfFile is not null)
                {
                    ELFFileKeyGenerator generator = new(Tracer.Instance, elfFile, fileName);
                    foreach (SymbolStoreKey key in generator.GetKeys(flags))
                    {
                        if (fileKey.Equals(key))
                        {
                            Trace.TraceInformation("DownloadELF: local file match {0}", fileName);
                            return fileName;
                        }
                    }
                }
            }

            // Now download the module from the symbol server if local file doesn't exists or doesn't have the right key
            string downloadFilePath = DownloadFile(fileKey);
            if (!string.IsNullOrEmpty(downloadFilePath))
            {
                Trace.TraceInformation("DownloadELF: downloaded {0}", downloadFilePath);
                return downloadFilePath;
            }

            return null;
        }

        /// <summary>
        /// Finds or downloads the MachO module.
        /// </summary>
        /// <param name="module">module instance</param>
        /// <param name="flags"></param>
        /// <returns>module path or null</returns>
        private string DownloadMachO(IModule module, KeyTypeFlags flags)
        {
            if ((flags & (KeyTypeFlags.IdentityKey | KeyTypeFlags.SymbolKey)) == 0)
            {
                throw new ArgumentException($"Key flag not supported {flags}");
            }

            if (module.BuildId.IsDefaultOrEmpty)
            {
                Trace.TraceWarning($"DownloadMachO: module {module.FileName} has no build id");
                return null;
            }

            SymbolStoreKey fileKey = MachOFileKeyGenerator.GetKeys(flags, module.FileName, module.BuildId.ToArray(), symbolFile: false, module.GetSymbolFileName()).SingleOrDefault();
            if (fileKey is null)
            {
                Trace.TraceWarning($"DownloadMachO: no index generated for module {module.FileName} ");
                return null;
            }

            // Check if the file is local and the key matches the module
            string fileName = fileKey.FullPathName;
            if (File.Exists(fileName))
            {
                using MachOFile machOFile = Utilities.OpenMachOFile(fileName);
                if (machOFile is not null)
                {
                    MachOFileKeyGenerator generator = new(Tracer.Instance, machOFile, fileName);
                    IEnumerable<SymbolStoreKey> keys = generator.GetKeys(flags);
                    foreach (SymbolStoreKey key in keys)
                    {
                        if (fileKey.Equals(key))
                        {
                            Trace.TraceInformation("DownloadMachO: local file match {0}", fileName);
                            return fileName;
                        }
                    }
                }
            }

            // Now download the module from the symbol server if local file doesn't exists or doesn't have the right key
            string downloadFilePath = DownloadFile(fileKey);
            if (!string.IsNullOrEmpty(downloadFilePath))
            {
                Trace.TraceInformation("DownloadMachO: downloaded {0}", downloadFilePath);
                return downloadFilePath;
            }

            return null;
        }

        /// <summary>
        /// Download a file from the symbol stores/server.
        /// </summary>
        /// <param name="key">index of the file to download</param>
        /// <returns>path to the downloaded file either in the cache or in the temp directory or null if error</returns>
        private string DownloadFile(SymbolStoreKey key)
        {
            string downloadFilePath = null;

            if (IsSymbolStoreEnabled)
            {
                using SymbolStoreFile file = GetSymbolStoreFile(key);
                if (file != null)
                {
                    try
                    {
                        downloadFilePath = file.FileName;

                        // Make sure the stream is at the beginning of the module
                        file.Stream.Position = 0;

                        // If the downloaded doesn't already exists on disk in the cache, then write it to a temporary location.
                        if (!File.Exists(downloadFilePath))
                        {
                            downloadFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + "-" + Path.GetFileName(key.FullPathName));
                            using (Stream destinationStream = File.OpenWrite(downloadFilePath))
                            {
                                file.Stream.CopyTo(destinationStream);
                            }
                            Trace.WriteLine($"Downloaded symbol file {key.FullPathName}");
                        }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
                    {
                        Trace.TraceError("{0}: {1}", file.FileName, ex.Message);
                        downloadFilePath = null;
                    }
                }
            }
            return downloadFilePath;
        }

        private static void ReadPortableDebugTableEntries(PEReader peReader, out DebugDirectoryEntry codeViewEntry, out DebugDirectoryEntry embeddedPdbEntry)
        {
            // See spec: https://github.com/dotnet/runtime/blob/main/docs/design/specs/PE-COFF.md

            codeViewEntry = default;
            embeddedPdbEntry = default;

            foreach (DebugDirectoryEntry entry in peReader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.CodeView)
                {
                    if (entry.MinorVersion != ImageDebugDirectory.PortablePDBMinorVersion)
                    {
                        continue;
                    }
                    codeViewEntry = entry;
                }
                else if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                {
                    embeddedPdbEntry = entry;
                }
            }
        }

        private SymbolFile TryOpenReaderFromCodeView(PEReader peReader, DebugDirectoryEntry codeViewEntry, string assemblyPath)
        {
            SymbolFile result = null;
            MetadataReaderProvider provider = null;
            try
            {
                CodeViewDebugDirectoryData data = peReader.ReadCodeViewDebugDirectoryData(codeViewEntry);
                string pdbPath = data.Path;
                Stream pdbStream = null;

                if (assemblyPath != null)
                {
                    try
                    {
                        pdbPath = Path.Combine(Path.GetDirectoryName(assemblyPath), GetFileName(pdbPath));
                    }
                    catch
                    {
                        // invalid characters in CodeView path
                        return null;
                    }
                    pdbStream = Utilities.TryOpenFile(pdbPath);
                }

                if (pdbStream == null)
                {
                    if (IsSymbolStoreEnabled)
                    {
                        Debug.Assert(codeViewEntry.MinorVersion == ImageDebugDirectory.PortablePDBMinorVersion);
                        SymbolStoreKey key = PortablePDBFileKeyGenerator.GetKey(pdbPath, data.Guid);
                        pdbStream = GetSymbolStoreFile(key)?.Stream;
                    }
                    if (pdbStream == null)
                    {
                        return null;
                    }
                    // Make sure the stream is at the beginning of the pdb.
                    pdbStream.Position = 0;
                }

                provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
                MetadataReader reader = provider.GetMetadataReader();

                // Validate that the PDB matches the assembly version
                if (data.Age == 1 && new BlobContentId(reader.DebugMetadataHeader.Id) == new BlobContentId(data.Guid, codeViewEntry.Stamp))
                {
                    result = new SymbolFile(provider, reader);
                }
            }
            catch (Exception e) when (e is BadImageFormatException or IOException)
            {
                return null;
            }
            finally
            {
                if (result == null)
                {
                    provider?.Dispose();
                }
            }

            return result;
        }

        private static SymbolFile TryOpenReaderFromEmbeddedPdb(PEReader peReader, DebugDirectoryEntry embeddedPdbEntry)
        {
            SymbolFile result = null;
            MetadataReaderProvider provider = null;

            try
            {
                // TODO: We might want to cache this provider globally (across stack traces),
                // since decompressing embedded PDB takes some time.
                provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedPdbEntry);
                result = new SymbolFile(provider, provider.GetMetadataReader());
            }
            catch (Exception e) when (e is BadImageFormatException or IOException)
            {
                return null;
            }
            finally
            {
                if (result == null)
                {
                    provider?.Dispose();
                }
            }

            return result;
        }

        /// <summary>
        /// Displays the symbol server and cache configuration
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new();

            sb.AppendLine("Current symbol store settings:");

            ForEachSymbolStore<SymbolStore.SymbolStores.SymbolStore>((symbolStore) => {
                if (symbolStore is HttpSymbolStore httpSymbolStore)
                {
                    sb.AppendLine($"-> {httpSymbolStore} Timeout: {httpSymbolStore.Timeout.Minutes} RetryCount: {httpSymbolStore.RetryCount}");
                }
                else
                {
                    sb.AppendLine($"-> {symbolStore}");
                }
            });
            return sb.ToString();
        }

        /// <summary>
        /// Returns true if cloud symweb server
        /// </summary>
        /// <param name="server"></param>
        private static bool IsSymweb(string server)
        {
            try
            {
                Uri uri = new(server);
                return uri.Host.Equals(_symwebHost, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to download/retrieve from cache the key.
        /// </summary>
        /// <param name="key">index of the file to retrieve</param>
        /// <returns>stream or null</returns>
        private SymbolStoreFile GetSymbolStoreFile(SymbolStoreKey key)
        {
            Debug.Assert(IsSymbolStoreEnabled);
            try
            {
                return _symbolStore.GetFile(key, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or BadImageFormatException or IOException)
            {
                Trace.TraceError("Exception: {0}", ex.ToString());
            }
            return null;
        }

        /// <summary>
        /// Sets a new store store head.
        /// </summary>
        /// <param name="store">symbol store (server, cache, directory, etc.)</param>
        private void SetSymbolStore(SymbolStore.SymbolStores.SymbolStore store)
        {
            if (store != _symbolStore)
            {
                OnChangeEvent.Fire();
                _symbolStore = store;
            }
        }

        private static bool IsDuplicateSymbolStore<T>(SymbolStore.SymbolStores.SymbolStore symbolStore, Func<T, bool> match)
            where T : SymbolStore.SymbolStores.SymbolStore
        {
            while (symbolStore != null)
            {
                if (symbolStore is T store)
                {
                    // TODO: replace this by adding an Equal override to the symbol stores
                    if (match(store))
                    {
                        return true;
                    }
                }
                symbolStore = symbolStore.BackingStore;
            }
            return false;
        }

        /// <summary>
        /// Enumerates the symbol stores.
        /// </summary>
        /// <typeparam name="T">type of symbol store or SymbolStore for all</typeparam>
        /// <param name="callback">called for each store found</param>
        public void ForEachSymbolStore<T>(Action<T> callback)
            where T : SymbolStore.SymbolStores.SymbolStore
        {
            SymbolStore.SymbolStores.SymbolStore symbolStore = _symbolStore;
            while (symbolStore != null)
            {
                if (symbolStore is T store)
                {
                    callback(store);
                }
                symbolStore = symbolStore.BackingStore;
            }
        }

        /// <summary>
        /// Quick fix for Path.GetFileName which incorrectly handles Windows-style paths on Linux
        /// </summary>
        /// <param name="pathName"> File path to be processed </param>
        /// <returns>Last component of path</returns>
        internal static string GetFileName(string pathName)
        {
            int pos = pathName.LastIndexOfAny(new char[] { '/', '\\' });
            if (pos < 0)
            {
                return pathName;
            }
            return pathName.Substring(pos + 1);
        }

        /// <summary>
        /// Compares two file paths using OS specific casing.
        /// </summary>
        private static bool IsPathEqual(string path1, string path2)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return StringComparer.OrdinalIgnoreCase.Equals(path1, path2);
            }
            else
            {
                return string.Equals(path1, path2);
            }
        }
    }
}
