// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress.FileSystem
{
    /// <summary>
    /// Egress provider for egressing stream data to the file system.
    /// </summary>
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

            if (!Directory.Exists(Options.DirectoryPath))
            {
                WrapException(() => Directory.CreateDirectory(Options.DirectoryPath));
            }

            string targetPath = Path.Combine(Options.DirectoryPath, name);

            if (!string.IsNullOrEmpty(Options.IntermediateDirectoryPath))
            {
                if (!Directory.Exists(Options.IntermediateDirectoryPath))
                {
                    WrapException(() => Directory.CreateDirectory(Options.IntermediateDirectoryPath));
                }

                string intermediateFilePath = null;
                try
                {
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
                        throw CreateException($"Unable to create unique intermediate file in '{Options.IntermediateDirectoryPath}' directory.");
                    }

                    await WriteFileAsync(action, intermediateFilePath, token);

                    WrapException(() => File.Move(intermediateFilePath, targetPath));
                }
                finally
                {
                    // Attempt to delete the intermediate file if it exists.
                    try
                    {
                        if (File.Exists(intermediateFilePath))
                        {
                            File.Delete(intermediateFilePath);
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

            Logger?.EgressProviderSavedStream(EgressProviderTypes.FileSystem, targetPath);
            return targetPath;
        }

        private void LogAndValidateOptions(string fileName)
        {
            Logger?.EgressProviderOptionValue(EgressProviderTypes.FileSystem, nameof(Options.DirectoryPath), Options.DirectoryPath);
            Logger?.EgressProviderOptionValue(EgressProviderTypes.FileSystem, nameof(Options.IntermediateDirectoryPath), Options.IntermediateDirectoryPath);
            Logger?.EgressProviderFileName(EgressProviderTypes.FileSystem, fileName);

            ValidateOptions();
        }

        private async Task WriteFileAsync(Func<Stream, CancellationToken, Task> action, string filePath, CancellationToken token)
        {
            using Stream fileStream = WrapException(
                () => new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None));

            Logger?.EgressProviderInvokeStreamAction(EgressProviderTypes.FileSystem);
            await action(fileStream, token);

            await fileStream.FlushAsync(token);
        }

        private static void WrapException(Action action)
        {
            WrapException(() => { action(); return true; });
        }

        private static T WrapException<T>(Func<T> func)
        {
            try
            {
                return func();
            }
            catch (DirectoryNotFoundException ex)
            {
                throw CreateException(ex);
            }
            catch (PathTooLongException ex)
            {
                throw CreateException(ex);
            }
            catch (IOException ex)
            {
                throw CreateException(ex);
            }
            catch (NotSupportedException ex)
            {
                throw CreateException(ex);
            }
            catch (SecurityException ex)
            {
                throw CreateException(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw CreateException(ex);
            }
        }

        private static EgressException CreateException(string message)
        {
            return new EgressException(WrapMessage(message));
        }

        private static EgressException CreateException(Exception innerException)
        {
            return new EgressException(WrapMessage(innerException.Message), innerException);
        }

        private static string WrapMessage(string innerMessage)
        {
            if (!string.IsNullOrEmpty(innerMessage))
            {
                return $"File system egress failed: {innerMessage}";
            }
            else
            {
                return "File system egress failed.";
            }
        }
    }
}