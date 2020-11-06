// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
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

            Logger?.LogDebug("Check if target directory exists.");
            if (!Directory.Exists(Options.DirectoryPath))
            {
                Logger?.LogDebug("Start creating target directory.");
                Directory.CreateDirectory(Options.DirectoryPath);
                Logger?.LogDebug("End creating target directory.");
            }

            string targetPath = Path.Combine(Options.DirectoryPath, name);

            if (!string.IsNullOrEmpty(Options.IntermediateDirectoryPath))
            {
                Logger?.LogDebug("Check if intermediate directory exists.");
                if (!Directory.Exists(Options.IntermediateDirectoryPath))
                {
                    Logger?.LogDebug("Start creating intermediate directory.");
                    Directory.CreateDirectory(Options.IntermediateDirectoryPath);
                    Logger?.LogDebug("End creating intermediate directory.");
                }

                string intermediateFilePath = null;
                try
                {
                    Logger?.LogDebug("Generating intermediate file.");
                    int remainingAttempts = 10;
                    bool intermediatePathExists;
                    do
                    {
                        intermediateFilePath = Path.Combine(Options.IntermediateDirectoryPath, Path.GetRandomFileName());
                        intermediatePathExists = File.Exists(intermediateFilePath);
                        remainingAttempts--;
                    }
                    while (intermediatePathExists && remainingAttempts > 0);

                    if (intermediatePathExists)
                    {
                        throw new InvalidOperationException($"Unable to create unique intermediate file in '{Options.IntermediateDirectoryPath}' directory.");
                    }

                    await WriteFileAsync(action, intermediateFilePath, token);

                    Logger?.LogDebug("Start moving intermediate file to destination.");
                    File.Move(intermediateFilePath, targetPath);
                    Logger?.LogDebug("End moving intermediate file to destination.");
                }
                finally
                {
                    // Attempt to delete the intermediate file if it exists.
                    try
                    {
                        Logger?.LogDebug("Check if intermediate file exists.");
                        if (File.Exists(intermediateFilePath))
                        {
                            Logger?.LogDebug("Start removing intermediate file.");
                            File.Delete(intermediateFilePath);
                            Logger?.LogDebug("End removing intermediate file.");
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
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
            Logger?.LogProviderOption(nameof(Options.IntermediateDirectoryPath), Options.IntermediateDirectoryPath);
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