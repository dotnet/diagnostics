// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Egress.FileSystem
{
    internal class FileSystemEgressEndpoint : EgressEndpoint<FileSystemEgressStreamOptions>
    {
        private readonly FileSystemEgressEndpointOptions _endpointOptions;

        public FileSystemEgressEndpoint(FileSystemEgressEndpointOptions endpointOptions)
        {
            _endpointOptions = endpointOptions;
        }

        public override async Task<string> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string name,
            FileSystemEgressStreamOptions streamOptions,
            CancellationToken token)
        {

            if (!Directory.Exists(_endpointOptions.DirectoryPath))
            {
                Directory.CreateDirectory(_endpointOptions.DirectoryPath);
            }

            string filePath = Path.Combine(_endpointOptions.DirectoryPath, name);

            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);

            await action(fileStream, token);

            await fileStream.FlushAsync(token);

            return filePath;
        }
    }
}