// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Egress.FileSystem
{
    internal class FileSystemEgressEndpoint : IEgressEndpoint<FileSystemEgressStreamOptions>
    {
        private readonly FileSystemEgressEndpointOptions _endpointOptions;

        public FileSystemEgressEndpoint(FileSystemEgressEndpointOptions endpointOptions)
        {
            _endpointOptions = endpointOptions;
        }

        public async Task<EgressResult> EgressAsync(
            Func<CancellationToken, Task<Stream>> action,
            string name,
            FileSystemEgressStreamOptions streamOptions,
            CancellationToken token)
        {
            using var fileStream = CreateFileStream(name, out string filePath);

            using Stream stream = await action(token);
            
            await stream.CopyToAsync(fileStream, 0x1000, token);

            await fileStream.FlushAsync(token);

            return new EgressResult("path", filePath);
        }

        public async Task<EgressResult> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string name,
            FileSystemEgressStreamOptions streamOptions,
            CancellationToken token)
        {
            using var fileStream = CreateFileStream(name, out string filePath);

            await action(fileStream, token);

            await fileStream.FlushAsync(token);

            return new EgressResult("path", filePath);
        }

        private FileStream CreateFileStream(string fileName, out string filePath)
        {
            if (!Directory.Exists(_endpointOptions.DirectoryPath))
            {
                Directory.CreateDirectory(_endpointOptions.DirectoryPath);
            }

            filePath = Path.Combine(_endpointOptions.DirectoryPath, fileName);

            return new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        }
    }
}