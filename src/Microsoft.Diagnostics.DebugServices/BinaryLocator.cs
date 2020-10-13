// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;
using SOS;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.DebugServices
{
    #nullable enable

    /// <summary>
    /// A symbol locator that search binaries based on files loaded in the live Linux target.
    /// </summary>
    public class BinaryLocator : IBinaryLocator
    {
        public BinaryLocator()
        {
        }

        public string? FindBinary(string fileName, int buildTimeStamp, int imageSize, bool checkProperties)
        {
            Trace.TraceInformation($"FindBinary: {fileName} buildTimeStamp {buildTimeStamp:X8} imageSize {imageSize:X8}");

            if (SymbolReader.IsSymbolStoreEnabled())
            {
                SymbolStoreKey? key = PEFileKeyGenerator.GetKey(fileName, (uint)buildTimeStamp, (uint)imageSize);
                if (key != null)
                {
                    // Now download the module from the symbol server, cache or from a directory
                    return SymbolReader.GetSymbolFile(key);
                }
                else
                {
                    Trace.TraceInformation($"DownloadFile: {fileName}: key not generated");
                }
            }
            else
            {
                Trace.TraceInformation($"DownLoadFile: {fileName}: symbol store not enabled");
            }

            return null;
        }

        public string? FindBinary(string fileName, ImmutableArray<byte> buildId, bool checkProperties)
        {
            Trace.TraceInformation($"FindBinary: {fileName} buildid {buildId}");
            return null;
        }

        public Task<string?> FindBinaryAsync(string fileName, ImmutableArray<byte> buildId, bool checkProperties)
        {
            Trace.TraceInformation($"FindBinaryAsync: {fileName} buildid {buildId}");
            return Task.FromResult<string?>(null);
        }

        public Task<string?> FindBinaryAsync(string fileName, int buildTimeStamp, int imageSize, bool checkProperties)
        {
            Trace.TraceInformation($"FindBinaryAsync: {fileName} buildTimeStamp {buildTimeStamp:X8} imageSize {imageSize:X8}");
            return Task.FromResult<string?>(null);
        }
    }
}
