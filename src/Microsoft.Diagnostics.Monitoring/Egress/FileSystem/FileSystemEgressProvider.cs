// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Egress.FileSystem
{
    internal class FileSystemEgressProvider :
        EgressProvider<FileSystemEgressProviderOptions, FileSystemEgressStreamOptions>
    {
        public FileSystemEgressProvider(FileSystemEgressProviderOptions options, ILogger logger = null)
            : base(options, logger)
        {
        }

        public override async Task<string> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string name,
            FileSystemEgressStreamOptions streamOptions,
            CancellationToken token)
        {
            LogAndValidateOptions(name);

            Logger?.LogDebug("Check if directory exists.");
            if (!Directory.Exists(Options.DirectoryPath))
            {
                Logger?.LogDebug("Start creating directory.");
                Directory.CreateDirectory(Options.DirectoryPath);
                Logger?.LogDebug("End creating directory.");
            }

            string targetPath = Path.Combine(Options.DirectoryPath, name);

            if (Options.UseIntermediateFile)
            {
                string intermediatePath = Path.Combine(Path.GetTempPath(), Path.GetTempFileName());

                await WriteFileAsync(action, intermediatePath, token);

                Logger?.LogDebug("Start moving intermediate file to destination.");
                File.Move(intermediatePath, targetPath);
                Logger?.LogDebug("End moving intermediate file to destination.");
            }
            else
            {
                await WriteFileAsync(action, targetPath, token);
            }

            Logger?.LogInformation("Saved stream to '{0}.", targetPath);
            return targetPath;
        }

        private void LogAndValidateOptions(string fileName)
        {
            Logger?.LogProviderOption(nameof(Options.DirectoryPath), Options.DirectoryPath);
            Logger?.LogProviderOption(nameof(Options.UseIntermediateFile), Options.UseIntermediateFile);
            Logger?.LogDebug($"File name: {fileName}");

            ValidateOptions();
        }

        private async Task WriteFileAsync(Func<Stream, CancellationToken, Task> action, string filePath, CancellationToken token)
        {
            Logger?.LogDebug("Opening file stream.");
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

            Logger?.LogDebug("Start writing to file.");

            Logger?.LogDebug("Start invoking stream action.");
            await action(fileStream, token);
            Logger?.LogDebug("End invoking stream action.");

            await fileStream.FlushAsync(token);
            Logger?.LogDebug("End writing to file.");
        }
    }
}