// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.FileFormats;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using SOS;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Symbol services to configure symbol servers, caches and directory search
    /// </summary>
    public class SymbolService : ISymbolService
    {
        private readonly IHost _host;

        public SymbolService(IHost host)
        {
            _host = host;
        }

        /// <summary>
        /// Invoked when anything changes in the symbol service (adding servers, caches, or directories, clearing store, etc.)
        /// </summary>
        public event ISymbolService.ChangeEventHandler OnChangeEvent;

        /// <summary>
        /// Returns true if symbol download has been enabled.
        /// </summary>
        public bool IsSymbolStoreEnabled => SymbolReader.IsSymbolStoreEnabled();

        /// <summary>
        /// The default symbol cache path:
        /// 
        /// * dbgeng on Windows uses the dbgeng symbol cache path: %PROGRAMDATA%\dbg\sym
        /// * dotnet-dump on Windows uses the VS symbol cache path: %TEMPDIR%\SymbolCache
        /// * dotnet-dump/lldb on Linux/MacOS uses: $HOME/.dotnet/symbolcache
        /// </summary>
        public string DefaultSymbolCache
        {
            get { return SymbolReader.DefaultSymbolCache; }
            set { SymbolReader.DefaultSymbolCache = value; }
        }

        /// <summary>
        /// Parses the Windows debugger symbol path (srv*, cache*, etc.).
        /// </summary>
        /// <param name="symbolPath">Windows symbol path</param>
        /// <returns>if false, error parsing symbol path</returns>
        public bool ParseSymbolPath(string symbolPath)
        {
            OnChangeEvent?.Invoke(this, new EventArgs());
            return SymbolReader.InitializeSymbolStore(
                logging: false,
                msdl: false,
                symweb: false, 
                tempDirectory: null,
                symbolServerPath: null,
                authToken: null,
                timeoutInMinutes: 0,
                symbolCachePath: null,
                symbolDirectoryPath: null,
                symbolPath);
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
            return SymbolReader.InitializeSymbolStore(
                logging: false,
                msdl,
                symweb, 
                tempDirectory: null,
                symbolServerPath,
                authToken,
                timeoutInMinutes,
                symbolCachePath: null,
                symbolDirectoryPath: null,
                windowsSymbolPath: null);
        }

        /// <summary>
        /// Add cache path to symbol search path
        /// </summary>
        /// <param name="symbolCachePath">symbol cache directory path (optional)</param>
        public void AddCachePath(string symbolCachePath)
        {
            if (symbolCachePath == null) throw new ArgumentNullException(nameof(symbolCachePath));

            SymbolReader.InitializeSymbolStore(
                logging: false,
                msdl: false,
                symweb: false, 
                tempDirectory: null,
                symbolServerPath: null,
                authToken: null,
                timeoutInMinutes: 0,
                symbolCachePath,
                symbolDirectoryPath: null,
                windowsSymbolPath: null);
        }

        /// <summary>
        /// Add directory path to symbol search path
        /// </summary>
        /// <param name="symbolDirectoryPath">symbol directory path to search (optional)</param>
        public void AddDirectoryPath(string symbolDirectoryPath)
        {
            if (symbolDirectoryPath == null) throw new ArgumentNullException(nameof(symbolDirectoryPath));

            SymbolReader.InitializeSymbolStore(
                logging: false,
                msdl: false,
                symweb: false, 
                tempDirectory: null,
                symbolServerPath: null,
                authToken: null,
                timeoutInMinutes: 0,
                symbolCachePath: null,
                symbolDirectoryPath,
                windowsSymbolPath: null);
        }

        /// <summary>
        /// This function disables any symbol downloading support.
        /// </summary>
        public void DisableSymbolStore()
        {
            SymbolReader.DisableSymbolStore();
        }

        /// <summary>
        /// Download a file from the symbol stores/server.
        /// </summary>
        /// <param name="key">index of the file to download</param>
        /// <returns>path to the downloaded file either in the cache or in the temp directory or null if error</returns>
        public string DownloadFile(SymbolStoreKey key)
        {
            return SymbolReader.GetSymbolFile(key);
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
                return SymbolReader.GetSymbolStoreFile(key);
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
            return sb.ToString();
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
