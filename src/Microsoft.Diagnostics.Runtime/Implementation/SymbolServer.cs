// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class SymbolServer : FileLocatorBase
    {
        public const string Msdl = "https://msdl.microsoft.com/download/symbols";

        private readonly FileSymbolCache _cache;
        private readonly HttpClient _http = new();

        public bool SupportsCompression { get; private set; } = true;
        public bool SupportsRedirection { get; private set; }

        public string Server { get; private set; }

        internal SymbolServer(FileSymbolCache cache, string server)
        {
            if (cache is null)
                throw new ArgumentNullException(nameof(cache));

            _cache = cache;
            Server = server;

            if (IsSymweb(server))
            {
                SupportsCompression = false;
                SupportsRedirection = true;
            }
        }

        private static bool IsSymweb(string server)
        {
            try
            {
                Uri uri = new(server);
                return uri.Host.Equals("symweb", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("symweb.corp.microsoft.com", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public override string? FindElfImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildId, bool checkProperties)
        {
            string? result = _cache.FindElfImage(fileName, archivedUnder, buildId, checkProperties);
            if (result != null)
                return result;

            string? key = base.FindElfImage(fileName, archivedUnder, buildId, checkProperties);
            if (key == null)
                return null;

            Stream? stream = FindFileOnServer(key).Result;
            if (stream != null)
                return _cache.Store(stream, key);

            return null;
        }

        public override string? FindMachOImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> uuid, bool checkProperties)
        {
            string? result = _cache.FindMachOImage(fileName, archivedUnder, uuid, checkProperties);
            if (result != null)
                return result;

            string? key = base.FindMachOImage(fileName, archivedUnder, uuid, checkProperties);
            if (key == null)
                return null;

            Stream? stream = FindFileOnServer(key).Result;
            if (stream != null)
                return _cache.Store(stream, key);

            return null;
        }

        public override string? FindPEImage(string fileName, int buildTimeStamp, int imageSize, bool checkProperties)
        {
            string? result = _cache.FindPEImage(fileName, buildTimeStamp, imageSize, checkProperties);
            if (result != null)
                return result;

            string? key = base.FindPEImage(fileName, buildTimeStamp, imageSize, checkProperties);
            if (key == null)
                return null;

            Stream? stream = FindFileOnServer(key).Result;
            if (stream != null)
                return _cache.Store(stream, key);

            return null;
        }

        public override string? FindPEImage(string fileName, SymbolProperties archivedUnder, ImmutableArray<byte> buildIdOrUUID, OSPlatform originalPlatform, bool checkProperties)
        {
            string? result = _cache.FindPEImage(fileName, archivedUnder, buildIdOrUUID, originalPlatform, checkProperties);
            if (result != null)
                return result;

            string? key = base.FindPEImage(fileName, archivedUnder, buildIdOrUUID, originalPlatform, checkProperties);
            if (key == null)
                return null;

            Stream? stream = FindFileOnServer(key).Result;
            if (stream != null)
                return _cache.Store(stream, key);

            return null;
        }

        private async Task<Stream?> FindFileOnServer(string key)
        {
            string fullPath = $"{Server}/{key.Replace('\\', '/')}";

            // If this server supports redirected files (E.G. symweb), then the vast majority of files are
            // archived via redirection.  Check that first before trying others to reduce requests.

            Task<string?> redirectedFile = Task.FromResult<string?>(null);
            if (SupportsRedirection)
            {
                int last = fullPath.LastIndexOfAny(new char[] { '/', '\\' }) + 1;

#pragma warning disable CA1845 // Use span-based 'string.Concat'. Not in NS2.0
                string filePtrPath = fullPath.Substring(0, last) + "file.ptr";
#pragma warning restore CA1845 // Use span-based 'string.Concat'
                redirectedFile = GetStringOrNull(filePtrPath);
            }

            string? path = await redirectedFile.ConfigureAwait(false);
            if (path is not null)
            {
                try
                {
                    if (path.StartsWith("PATH:", StringComparison.Ordinal))
                    {
                        path = path.Substring(5);

                        if (File.Exists(path))
                            return File.OpenRead(path);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                }
            }

            Task<HttpResponseMessage> file = _http.GetAsync(fullPath);
            Task<HttpResponseMessage?> compressed = Task.FromResult<HttpResponseMessage?>(null);

#pragma warning disable CA1845 // Use span-based 'string.Concat'. Not in NS2.0.
            string compressedPath = fullPath.Substring(0, fullPath.Length - 1) + '_';
#pragma warning restore CA1845 // Use span-based 'string.Concat'
            if (SupportsCompression)
                compressed = _http.GetAsync(compressedPath)!;

            HttpResponseMessage fileResponse = await file.ConfigureAwait(false);
            if (fileResponse.IsSuccessStatusCode)
                return await fileResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
            else
                fileResponse.Dispose();

            HttpResponseMessage? compressedResponse = await compressed.ConfigureAwait(false);
            if (compressedResponse is not null && compressedResponse.IsSuccessStatusCode)
            {
                string tmpPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(compressedPath));
                string output = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(fullPath));

                Command.Run("Expand " + Command.Quote(tmpPath) + " " + Command.Quote(output));
                MemoryStream ms = new();
                using (FileStream fs = File.OpenRead(output))
                    await fs.CopyToAsync(ms).ConfigureAwait(false);

                ms.Position = 0;
                try
                {
                    if (File.Exists(output))
                        File.Delete(output);

                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                }

                return ms;
            }

            return null;
        }

        private async Task<string?> GetStringOrNull(string filePtrPath)
        {
            try
            {
                return await _http.GetStringAsync(filePtrPath).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }
    }
}
