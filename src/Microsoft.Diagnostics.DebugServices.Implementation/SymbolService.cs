// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using Microsoft.SymbolStore.SymbolStores;
using SOS;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
        public const string MsdlSymbolServer = "http://msdl.microsoft.com/download/symbols/";
        public const string SymwebSymbolServer = "http://symweb.corp.microsoft.com/";

        private readonly IHost _host;
        private string _defaultSymbolCache;
        private Microsoft.SymbolStore.SymbolStores.SymbolStore _symbolStore = null;

        public SymbolService(IHost host)
        {
            _host = host;
            OnChangeEvent = new ServiceEvent();
        }

        /// <summary>
        /// Invoked when anything changes in the symbol service (adding servers, caches, or directories, clearing store, etc.)
        /// </summary>
        public IServiceEvent OnChangeEvent { get; }

        /// <summary>
        /// Returns true if symbol download has been enabled.
        /// </summary>
        public bool IsSymbolStoreEnabled => _symbolStore != null;

        /// <summary>
        /// The default symbol cache path:
        /// 
        /// * dbgeng on Windows uses the dbgeng symbol cache path: %PROGRAMDATA%\dbg\sym
        /// * dotnet-dump on Windows uses the VS symbol cache path: %TEMPDIR%\SymbolCache
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
                            HostType.DotnetDump => Path.Combine(Path.GetTempPath(), "SymbolCache"),
                            _ => throw new NotSupportedException($"Host type not supported {_host.HostType}"),
                        };
                    }
                    else
                    {
                        _defaultSymbolCache = Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".dotnet", "symbolcache");
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
                        if (!AddSymbolServer(msdl: false, symweb: false, symbolServerPath.Trim(), authToken: null, timeoutInMinutes: 0))
                        {
                            return false;
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
        /// Add symbol server to search path.
        /// </summary>
        /// <param name="msdl">if true, use the public Microsoft server</param>
        /// <param name="symweb">if true, use symweb internal server and protocol (file.ptr)</param>
        /// <param name="symbolServerPath">symbol server url (optional)</param>
        /// <param name="authToken"></param>
        /// <param name="timeoutInMinutes">symbol server timeout in minutes (optional)</param>
        /// <returns>if false, failure</returns>
        public bool AddSymbolServer(
            bool msdl,
            bool symweb,
            string symbolServerPath,
            string authToken,
            int timeoutInMinutes)
        {
            bool internalServer = false;

            // Add symbol server URL if exists
            if (symbolServerPath == null)
            {
                if (msdl)
                {
                    symbolServerPath = MsdlSymbolServer;
                }
                else if (symweb)
                {
                    symbolServerPath = SymwebSymbolServer;
                    internalServer = true;
                }
            }
            else
            {
                // Use the internal symbol store for symweb
                internalServer = symbolServerPath.Contains("symweb");
            }

            // Return error if symbol server path is null and msdl and symweb are false.
            if (symbolServerPath == null)
            {
                return false;
            }

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
                Microsoft.SymbolStore.SymbolStores.SymbolStore store = _symbolStore;
                if (!IsDuplicateSymbolStore<HttpSymbolStore>(store, (httpSymbolStore) => uri.Equals(httpSymbolStore.Uri)))
                {
                    // Create http symbol server store
                    HttpSymbolStore httpSymbolStore;
                    if (internalServer)
                    {
                        httpSymbolStore = new SymwebHttpSymbolStore(Tracer.Instance, store, uri);
                    }
                    else
                    {
                        httpSymbolStore = new HttpSymbolStore(Tracer.Instance, store, uri, personalAccessToken: authToken);
                    }
                    if (timeoutInMinutes != 0)
                    {
                        httpSymbolStore.Timeout = TimeSpan.FromMinutes(timeoutInMinutes);
                    }
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
            if (symbolCachePath == null) throw new ArgumentNullException(nameof(symbolCachePath));

            Microsoft.SymbolStore.SymbolStores.SymbolStore store = _symbolStore;
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
            if (symbolDirectoryPath == null) throw new ArgumentNullException(nameof(symbolDirectoryPath));

            Microsoft.SymbolStore.SymbolStores.SymbolStore store = _symbolStore;
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
        /// Download a file from the symbol stores/server.
        /// </summary>
        /// <param name="key">index of the file to download</param>
        /// <returns>path to the downloaded file either in the cache or in the temp directory or null if error</returns>
        public string DownloadFile(SymbolStoreKey key)
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
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
                    {
                        Trace.TraceError("{0}: {1}", file.FileName, ex.Message);
                        downloadFilePath = null;
                    }
                }
            }
            return downloadFilePath;
        }

        /// <summary>
        /// Attempts to download/retrieve from cache the key.
        /// </summary>
        /// <param name="key">index of the file to retrieve</param>
        /// <returns>stream or null</returns>
        public SymbolStoreFile GetSymbolStoreFile(SymbolStoreKey key)
        {
            if (IsSymbolStoreEnabled)
            {
                try
                {
                    return _symbolStore.GetFile(key, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is BadImageFormatException || ex is IOException)
                {
                    Trace.TraceError("Exception: {0}", ex.ToString());
                }
            }
            return null;
        }

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
                    peStream = TryOpenFile(imagePath);
                }
                else if (IsSymbolStoreEnabled)
                {
                    SymbolStoreKey key = PEFileKeyGenerator.GetKey(imagePath, imageTimestamp, imageSize);
                    peStream = GetSymbolStoreFile(key)?.Stream;
                }
                if (peStream != null)
                {
                    using var peReader = new PEReader(peStream, PEStreamOptions.Default);
                    if (peReader.HasMetadata)
                    {
                        PEMemoryBlock metadataInfo = peReader.GetMetadata();
                        return metadataInfo.GetContent();
                    }
                }
            }
            catch (Exception ex) when 
                (ex is UnauthorizedAccessException || 
                 ex is BadImageFormatException || 
                 ex is InvalidVirtualAddressException || 
                 ex is IOException)
            {
                Trace.TraceError($"GetMetaData: {ex.Message}");
            }
            return ImmutableArray<byte>.Empty;
        }

        /// <summary>
        /// Displays the symbol server and cache configuration
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            Microsoft.SymbolStore.SymbolStores.SymbolStore symbolStore = _symbolStore;
            while (symbolStore != null)
            {
                sb.AppendLine(symbolStore.ToString());
                symbolStore = symbolStore.BackingStore;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Sets a new store store head.
        /// </summary>
        /// <param name="store">symbol store (server, cache, directory, etc.)</param>
        private void SetSymbolStore(Microsoft.SymbolStore.SymbolStores.SymbolStore store)
        {
            if (store != _symbolStore)
            {
                OnChangeEvent.Fire();
                _symbolStore = store;
            }
        }

        private bool IsDuplicateSymbolStore<T>(Microsoft.SymbolStore.SymbolStores.SymbolStore symbolStore, Func<T, bool> match) 
            where T : Microsoft.SymbolStore.SymbolStores.SymbolStore
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

        /// <summary>
        /// Attempt to open a file stream.
        /// </summary>
        /// <param name="path">file path</param>
        /// <returns>stream or null if doesn't exist or error</returns>
        private Stream TryOpenFile(string path)
        {
            if (File.Exists(path))
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
