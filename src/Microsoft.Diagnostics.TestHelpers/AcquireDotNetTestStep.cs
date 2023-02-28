// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// Acquires the CLI tools from a web endpoint, a local zip/tar.gz, or directly from a local path
    /// </summary>
    public class AcquireDotNetTestStep : TestStep
    {
        /// <summary>
        /// Create a new AcquireDotNetTestStep
        /// </summary>
        /// <param name="remoteDotNetZipPath">
        /// If non-null, the CLI tools will be downloaded from this web endpoint.
        /// The path should use an http or https scheme and the remote file should be in .zip or .tar.gz format.
        /// localDotNetZipPath must also be non-null to indicate where the downloaded archive will be cached</param>
        /// <param name="localDotNetZipPath">
        /// If non-null, the location of a .zip or .tar.gz compressed folder containing the CLI tools. This
        /// must be a local file system or network file system path.
        /// localDotNetZipExpandDirPath must also be non-null to indicate where the expanded folder will be
        /// stored.
        /// localDotNetTarPath must be non-null if localDotNetZip points to a .tar.gz format archive, in order
        /// to indicate where the .tar file will be cached</param>
        /// <param name="localDotNetTarPath">
        /// If localDotNetZipPath points to a .tar.gz, this path will be used to store the uncompressed .tar
        /// file. Otherwise this path is unused.</param>
        /// <param name="localDotNetZipExpandDirPath">
        /// If localDotNetZipPath is non-null, this path will be used to store the expanded version of the
        /// archive. Otherwise this path is unused.</param>
        /// <param name="localDotNetPath">
        /// The path to the dotnet binary. When the CLI tools are being acquired from a compressed archive
        /// this will presumably be a path inside the localDotNetZipExpandDirPath directory, otherwise
        /// it can be any local file system path where the dotnet binary can be found.</param>
        /// <param name="logFilePath">
        /// The path where an activity log for this test step should be written.
        /// </param>
        ///
        public AcquireDotNetTestStep(
            string remoteDotNetZipPath,
            string localDotNetZipPath,
            string localDotNetTarPath,
            string localDotNetZipExpandDirPath,
            string localDotNetPath,
            string logFilePath)
            : base(logFilePath, "Acquire DotNet Tools")
        {
            RemoteDotNetPath = remoteDotNetZipPath;
            LocalDotNetZipPath = localDotNetZipPath;
            if (localDotNetZipPath != null && localDotNetZipPath.EndsWith(".tar.gz"))
            {
                LocalDotNetTarPath = localDotNetTarPath;
            }
            if (localDotNetZipPath != null)
            {
                LocalDotNetZipExpandDirPath = localDotNetZipExpandDirPath;
            }
            LocalDotNetPath = localDotNetPath;
        }

        /// <summary>
        /// If non-null, the CLI tools will be downloaded from this web endpoint.
        /// The path should use an http or https scheme and the remote file should be in .zip or .tar.gz format.
        /// </summary>
        public string RemoteDotNetPath { get; private set; }

        /// <summary>
        /// If non-null, the location of a .zip or .tar.gz compressed folder containing the CLI tools. This
        /// is a local file system or network file system path.
        /// </summary>
        public string LocalDotNetZipPath { get; private set; }

        /// <summary>
        /// If localDotNetZipPath points to a .tar.gz, this path will be used to store the uncompressed .tar
        /// file. Otherwise null.
        /// </summary>
        public string LocalDotNetTarPath { get; private set; }

        /// <summary>
        /// If localDotNetZipPath is non-null, this path will be used to store the expanded version of the
        /// archive. Otherwise null.
        /// </summary>
        public string LocalDotNetZipExpandDirPath { get; private set; }

        /// <summary>
        /// The path to the dotnet binary when the test step is complete.
        /// </summary>
        public string LocalDotNetPath { get; private set; }

        /// <summary>
        /// Returns true, if there any actual work to do (like downloading, unziping or untaring).
        /// </summary>
        public bool AnyWorkToDo { get { return RemoteDotNetPath != null || LocalDotNetZipPath != null; } }

        async protected override Task DoWork(ITestOutputHelper output)
        {
            if (RemoteDotNetPath != null)
            {
                await DownloadFile(RemoteDotNetPath, LocalDotNetZipPath, output);
            }
            if (LocalDotNetZipPath != null)
            {
                if (LocalDotNetZipPath.EndsWith(".zip"))
                {
                    await Unzip(LocalDotNetZipPath, LocalDotNetZipExpandDirPath, output);
                }
                else if (LocalDotNetZipPath.EndsWith(".tar.gz"))
                {
                    await UnGZip(LocalDotNetZipPath, LocalDotNetTarPath, output);
                    await Untar(LocalDotNetTarPath, LocalDotNetZipExpandDirPath, output);
                }
                else
                {
                    output.WriteLine("Unsupported compression format: " + LocalDotNetZipPath);
                    throw new NotSupportedException("Unsupported compression format: " + LocalDotNetZipPath);
                }
            }
            output.WriteLine("Dotnet path: " + LocalDotNetPath);
            if (!File.Exists(LocalDotNetPath))
            {
                throw new FileNotFoundException(LocalDotNetPath + " not found");
            }
        }

        private static async Task DownloadFile(string remotePath, string localPath, ITestOutputHelper output)
        {
            output.WriteLine("Downloading: " + remotePath + " -> " + localPath);
            using HttpClient client = new();
            using HttpResponseMessage response = await client.GetAsync(remotePath);
            response.EnsureSuccessStatusCode();
            using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath));
            using FileStream localZipStream = File.OpenWrite(localPath);
            // TODO: restore the CopyToAsync code after System.Net.Http.dll is
            // updated to a newer version. The current old version has a bug
            // where the copy never finished.
            await stream.CopyToAsync(localZipStream);
            output.WriteLine("Downloading finished");
        }

        private static async Task UnGZip(string gzipPath, string expandedFilePath, ITestOutputHelper output)
        {
            output.WriteLine("Unziping: " + gzipPath + " -> " + expandedFilePath);
            using (FileStream gzipStream = File.OpenRead(gzipPath))
            {
                using (GZipStream expandedStream = new GZipStream(gzipStream, CompressionMode.Decompress))
                {
                    using (FileStream targetFileStream = File.OpenWrite(expandedFilePath))
                    {
                        await expandedStream.CopyToAsync(targetFileStream);
                    }
                }
            }
        }

        private static async Task Unzip(string zipPath, string expandedDirPath, ITestOutputHelper output)
        {
            output.WriteLine("Unziping: " + zipPath + " -> " + expandedDirPath);
            using (FileStream zipStream = File.OpenRead(zipPath))
            {
                ZipArchive zip = new ZipArchive(zipStream);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string extractedFilePath = Path.Combine(expandedDirPath, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(extractedFilePath));
                    using (Stream zipFileStream = entry.Open())
                    {
                        using (FileStream extractedFileStream = File.OpenWrite(extractedFilePath))
                        {
                            await zipFileStream.CopyToAsync(extractedFileStream);
                        }
                    }
                }
            }
        }

        private static async Task Untar(string tarPath, string expandedDirPath, ITestOutputHelper output)
        {
            Directory.CreateDirectory(expandedDirPath);
            string tarToolPath = null;
            if (OS.Kind == OSKind.Linux)
            {
                tarToolPath = "/bin/tar";
            }
            else if (OS.Kind == OSKind.OSX)
            {
                tarToolPath = "/usr/bin/tar";
            }
            else
            {
                throw new NotSupportedException("Unknown where this OS stores the tar executable");
            }

            await new ProcessRunner(tarToolPath, "-xf " + tarPath).
                   WithWorkingDirectory(expandedDirPath).
                   WithLog(output).
                   WithExpectedExitCode(0).
                   Run();
        }

    }
}
