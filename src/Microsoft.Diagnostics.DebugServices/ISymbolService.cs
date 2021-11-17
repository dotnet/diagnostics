// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.SymbolStore;
using System.Collections.Immutable;

namespace Microsoft.Diagnostics.DebugServices
{
    public interface ISymbolService
    {
        /// <summary>
        /// Invoked when anything changes in the symbol service (adding servers, caches, or directories, clearing store, etc.)
        /// </summary>
        IServiceEvent OnChangeEvent { get; }

        /// <summary>
        /// Returns true if symbol download has been enabled.
        /// </summary>
        bool IsSymbolStoreEnabled { get; }

        /// <summary>
        /// The default symbol cache path:
        /// 
        /// * dbgeng on Windows uses the dbgeng symbol cache path: %PROGRAMDATA%\dbg\sym
        /// * dotnet-dump on Windows uses the VS symbol cache path: %TEMPDIR%\SymbolCache
        /// * dotnet-dump/lldb on Linux/MacOS uses: $HOME/.dotnet/symbolcache
        /// </summary>
        string DefaultSymbolCache { get; set; }

        /// <summary>
        /// Parses the Windows debugger symbol path (srv*, cache*, etc.).
        /// </summary>
        /// <param name="symbolPath">Windows symbol path</param>
        /// <returns>if false, error parsing symbol path</returns>
        bool ParseSymbolPath(string symbolPath);

        /// <summary>
        /// Add symbol server to search path.
        /// </summary>
        /// <param name="msdl">if true, use the public Microsoft server</param>
        /// <param name="symweb">if true, use symweb internal server and protocol (file.ptr)</param>
        /// <param name="symbolServerPath">symbol server url (optional)</param>
        /// <param name="authToken"></param>
        /// <param name="timeoutInMinutes">symbol server timeout in minutes (optional)</param>
        /// <returns>if false, failure</returns>
        bool AddSymbolServer(bool msdl, bool symweb, string symbolServerPath, string authToken, int timeoutInMinutes);

        /// <summary>
        /// Add cache path to symbol search path
        /// </summary>
        /// <param name="symbolCachePath">symbol cache directory path (optional)</param>
        void AddCachePath(string symbolCachePath);

        /// <summary>
        /// Add directory path to symbol search path
        /// </summary>
        /// <param name="symbolDirectoryPath">symbol directory path to search (optional)</param>
        void AddDirectoryPath(string symbolDirectoryPath);

        /// <summary>
        /// This function disables any symbol downloading support.
        /// </summary>
        void DisableSymbolStore();

        /// <summary>
        /// Download a file from the symbol stores/server.
        /// </summary>
        /// <param name="key">index of the file to download</param>
        /// <returns>path to the downloaded file either in the cache or in the temp directory or null if error</returns>
        string DownloadFile(SymbolStoreKey key);

        /// <summary>
        /// Attempts to download/retrieve from cache the key.
        /// </summary>
        /// <param name="key">index of the file to retrieve</param>
        /// <returns>stream or null</returns>
        SymbolStoreFile GetSymbolStoreFile(SymbolStoreKey key);

        /// <summary>
        /// Returns the metadata for the assembly
        /// </summary>
        /// <param name="imagePath">file name and path to module</param>
        /// <param name="imageTimestamp">module timestamp</param>
        /// <param name="imageSize">size of PE image</param>
        /// <returns>metadata</returns>
        ImmutableArray<byte> GetMetadata(string imagePath, uint imageTimestamp, uint imageSize);
    }
}
