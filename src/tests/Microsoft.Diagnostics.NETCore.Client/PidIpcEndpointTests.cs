// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class PidIpcEndpointTests
    {
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsNotLinux => !IsLinux;
        public static bool IsUnix => IsLinux || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        [Theory]
        [InlineData("Uid:\t1000\t1001\t1002\t1003", 1003u)]
        [InlineData("Uid:  0  0  0  0", 0u)]
        [InlineData("Uid:\t1\t2\t3\t4294967295", uint.MaxValue)]
        public void ParseLinuxUid_UsesFilesystemUid(string line, uint expected)
        {
            Assert.Equal(expected, UnixSocketOwner.ParseLinuxUid(line));
        }

        [Theory]
        [InlineData("Uid:\t1\t2\t3")]
        [InlineData("Uid:\t1\t2\t3\t-1")]
        [InlineData("Uid:\t1\t2\t3\t4294967296")]
        [InlineData("Uid:\t1\t2\t3\tinvalid")]
        [InlineData("Gid:\t1\t2\t3\t4")]
        public void ParseLinuxUid_InvalidStatus_Throws(string line)
        {
            Assert.Throws<IOException>(() => UnixSocketOwner.ParseLinuxUid(line));
        }

        [ConditionalTheory(nameof(IsUnix))]
        [InlineData("socket")]
        [InlineData("socket-\u00e9")]
        public void IsSocketOwnedBy_RejectsWrongUidRegularFileSymlinkAndMissingFile(string socketName)
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                uint uid = UnixSocketOwner.GetProcessUid(Environment.ProcessId);
                string path = Path.Combine(directory, socketName);
                using Socket socket = BindSocket(path);
                Assert.True(UnixSocketOwner.IsSocketOwnedBy(path, uid));
                Assert.False(UnixSocketOwner.IsSocketOwnedBy(path, uid == 0 ? 1u : 0u));

                string regularFile = Path.Combine(directory, "regular");
                File.WriteAllText(regularFile, "");
                Assert.False(UnixSocketOwner.IsSocketOwnedBy(regularFile, uid));

                string link = Path.Combine(directory, "link");
                File.CreateSymbolicLink(link, path);
                Assert.False(UnixSocketOwner.IsSocketOwnedBy(link, uid));
                Assert.False(UnixSocketOwner.IsSocketOwnedBy(directory, uid));
                File.Delete(link);
                File.CreateSymbolicLink(link, Path.Combine(directory, "missing"));
                Assert.False(UnixSocketOwner.IsSocketOwnedBy(link, uid));
                Assert.False(UnixSocketOwner.IsSocketOwnedBy(Path.Combine(directory, "missing"), uid));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalFact(nameof(IsUnix))]
        public void TryResolveAddress_FiltersBeforeSortingAndUsesHostPid()
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                const int filenamePid = 123;
                string older = Path.Combine(directory, $"dotnet-diagnostic-{filenamePid}-1-socket");
                string newer = Path.Combine(directory, $"dotnet-diagnostic-{filenamePid}-2-socket");
                string regular = Path.Combine(directory, $"dotnet-diagnostic-{filenamePid}-3-socket");
                string link = Path.Combine(directory, $"dotnet-diagnostic-{filenamePid}-4-socket");
                using Socket olderSocket = BindSocket(older);
                using Socket newerSocket = BindSocket(newer);
                File.WriteAllText(regular, "");
                File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-3));
                File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-2));
                File.CreateSymbolicLink(link, regular);

                Assert.True(PidIpcEndpoint.TryResolveAddress(directory, filenamePid, out string address, Environment.ProcessId));
                Assert.Equal(newer, address);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalFact(nameof(IsUnix))]
        public void TryResolveAddress_WrongOwnerSocketIsNotSelected()
        {
            if (UnixSocketOwner.GetProcessUid(Environment.ProcessId) != 0)
            {
                Assert.Skip("Changing socket ownership requires root.");
            }

            string directory = CreateSocketTestDirectory();
            try
            {
                string valid = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-1-socket");
                string wrongOwner = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-2-socket");
                using Socket validSocket = BindSocket(valid);
                using Socket wrongOwnerSocket = BindSocket(wrongOwner);
                File.SetLastWriteTimeUtc(valid, DateTime.UtcNow.AddMinutes(-2));
                Assert.Equal(0, Chown(wrongOwner, 1, uint.MaxValue));
                Assert.True(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out string address));
                Assert.Equal(valid, address);

                string routerPath = Path.Combine(directory, $"dotnet-diagnostic-dsrouter-{Environment.ProcessId}-1-socket");
                using Socket router = BindSocket(routerPath);
                Assert.Equal(0, Chown(routerPath, 1, uint.MaxValue));
                Assert.True(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out address));
                Assert.Equal(routerPath, address);

                validSocket.Dispose();
                File.Delete(valid);
                Assert.False(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out address));
                Assert.Null(address);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalFact(nameof(IsUnix))]
        public void TryResolveAddress_NoValidRuntimeSocket_DoesNotSelectRouter()
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                string invalid = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-1-socket");
                File.WriteAllText(invalid, "");
                using Socket router = BindSocket(Path.Combine(directory, $"dotnet-diagnostic-dsrouter-{Environment.ProcessId}-1-socket"));
                Assert.False(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out string address));
                Assert.Null(address);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalFact(nameof(IsUnix))]
        public void TryResolveAddress_PreservesRouterTimestampSelection()
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                string runtimePath = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-1-socket");
                string routerPath = Path.Combine(directory, $"dotnet-diagnostic-dsrouter-{Environment.ProcessId}-1-socket");
                using Socket runtime = BindSocket(runtimePath);
                using Socket router = BindSocket(routerPath);
                DateTime timestamp = DateTime.UtcNow.AddMinutes(-2);
                File.SetLastWriteTimeUtc(runtimePath, timestamp);
                File.SetLastWriteTimeUtc(routerPath, timestamp.AddMinutes(-1));
                Assert.True(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out string address));
                Assert.Equal(runtimePath, address);

                File.SetLastWriteTimeUtc(routerPath, timestamp);
                Assert.True(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out address));
                Assert.Equal(routerPath, address);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalFact(nameof(IsUnix))]
        public void TryResolveAddress_UnreadableTargetCredentials_FailsClosed()
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                using Socket socket = BindSocket(Path.Combine(directory, "dotnet-diagnostic-123-1-socket"));
                Assert.False(PidIpcEndpoint.TryResolveAddress(directory, 123, out string address, int.MaxValue));
                Assert.Null(address);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalFact(nameof(IsUnix))]
        public void IsSocketOwnedBy_StatFailure_ThrowsWithPath()
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                string file = Path.Combine(directory, "regular");
                File.WriteAllText(file, "");
                string invalid = Path.Combine(file, "socket");
                IOException error = Assert.Throws<IOException>(() => UnixSocketOwner.IsSocketOwnedBy(invalid, 0));
                Assert.Contains(invalid, error.Message);
                Assert.IsType<System.ComponentModel.Win32Exception>(error.InnerException);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalFact(nameof(IsUnix))]
        public void GetPublishedProcesses_RegularFileDoesNotPublishProcess()
        {
            using Process child = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false });
            string path = Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-{child.Id}-123456789-socket");
            try
            {
                File.WriteAllText(path, "");
                Assert.DoesNotContain(child.Id, DiagnosticsClient.GetPublishedProcesses());
            }
            finally
            {
                File.Delete(path);
                child.Kill();
                child.WaitForExit();
            }
        }

        [ConditionalTheory(nameof(IsUnix))]
        [InlineData(1)]
        [InlineData(1000)]
        public void GetLocalPublishedProcesses_ValidatesCandidatesAndDeduplicatesProcesses(int regularFileCount)
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                for (int i = 0; i < regularFileCount; i++)
                {
                    File.WriteAllText(Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-{i}-socket"), "");
                }

                string validPath = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-{regularFileCount}-socket");
                string link = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-{regularFileCount + 1}-socket");
                File.CreateSymbolicLink(link, validPath);
                Assert.Empty(DiagnosticsClient.GetLocalPublishedProcesses(directory));

                using Socket socket = BindSocket(validPath);
                using Socket secondSocket = BindSocket(Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-{regularFileCount + 2}-socket"));
                Assert.Equal(new[] { Environment.ProcessId }, DiagnosticsClient.GetLocalPublishedProcesses(directory));

                socket.Dispose();
                secondSocket.Dispose();
                File.Delete(validPath);
                File.Delete(Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-{regularFileCount + 2}-socket"));
                Assert.Empty(DiagnosticsClient.GetLocalPublishedProcesses(directory));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void GetProcessInfo_CurrentProcess_ConnectsThroughDefaultEndpoint()
        {
            DiagnosticsClient client = new(Environment.ProcessId);
            ProcessInfo info = client.GetProcessInfo();
            Assert.Equal((ulong)Environment.ProcessId, info.ProcessId);
            Assert.Contains(Environment.ProcessId, DiagnosticsClient.GetPublishedProcesses());
        }

        [ConditionalFact(nameof(IsNotLinux))]
        public void TryResolveAddress_Windows_NamedPipeAddressUnchanged()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Skip("Windows named pipes only.");
            }
            Assert.True(PidIpcEndpoint.TryResolveAddress(@"\\.\pipe\", Environment.ProcessId, out string address));
            Assert.Equal($"dotnet-diagnostic-{Environment.ProcessId}", address);
        }

        private static string CreateSocketTestDirectory()
        {
            // Keep paths short enough for macOS's Unix socket path limit.
            string directory = Path.Combine("/tmp", "ipc-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static Socket BindSocket(string path)
        {
            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Bind(new UnixDomainSocketEndPoint(path));
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

#pragma warning disable SYSLIB1054
        [DllImport("libc", EntryPoint = "chown", SetLastError = true)]
        private static extern int Chown(string path, uint uid, uint gid);
#pragma warning restore SYSLIB1054

        #region Parsing tests (platform-independent, synthetic data)

        // Realistic /proc/{pid}/status content based on Docker cross-container testing.
        // The NSpid line shows host PID 2209 mapped to container PID 1.
        private static readonly string[] CrossNamespaceStatus = new[]
        {
            "Name:\tdotnet",
            "Umask:\t0022",
            "State:\tS (sleeping)",
            "Tgid:\t2209",
            "Ngid:\t0",
            "Pid:\t2209",
            "PPid:\t2189",
            "NSpid:\t2209\t1",
            "Threads:\t15",
        };

        private static readonly string[] SameNamespaceStatus = new[]
        {
            "Name:\tdotnet",
            "State:\tS (sleeping)",
            "Pid:\t500",
            "NSpid:\t500",
            "Threads:\t10",
        };

        private static readonly string[] NestedContainerStatus = new[]
        {
            "Name:\tdotnet",
            "Pid:\t680",
            "NSpid:\t680\t50\t1",
        };

        [Fact]
        public void TryParseNamespacePid_CrossNamespace_ReturnsTrueWithContainerPid()
        {
            bool result = PidIpcEndpoint.TryParseNamespacePid(CrossNamespaceStatus, 2209, out int nsPid);
            Assert.True(result);
            Assert.Equal(1, nsPid);
        }

        [Fact]
        public void TryParseNamespacePid_SameNamespace_ReturnsFalse()
        {
            bool result = PidIpcEndpoint.TryParseNamespacePid(SameNamespaceStatus, 500, out int nsPid);
            Assert.False(result);
            Assert.Equal(500, nsPid);
        }

        [Fact]
        public void TryParseNamespacePid_NestedContainers_ReturnsInnermostPid()
        {
            bool result = PidIpcEndpoint.TryParseNamespacePid(NestedContainerStatus, 680, out int nsPid);
            Assert.True(result);
            Assert.Equal(1, nsPid);
        }

        [Fact]
        public void TryParseNamespacePid_NoNSpidLine_ReturnsFalse()
        {
            string[] status = new[] { "Name:\tdotnet", "Pid:\t100", "Threads:\t5" };
            bool result = PidIpcEndpoint.TryParseNamespacePid(status, 100, out int nsPid);
            Assert.False(result);
            Assert.Equal(100, nsPid);
        }

        [Fact]
        public void TryParseNamespacePid_EmptyInput_ReturnsFalse()
        {
            bool result = PidIpcEndpoint.TryParseNamespacePid(new string[0], 100, out int nsPid);
            Assert.False(result);
            Assert.Equal(100, nsPid);
        }

        [Fact]
        public void TryParseNamespacePid_MalformedNSpid_ReturnsFalse()
        {
            string[] status = new[] { "NSpid:\tabc\txyz" };
            bool result = PidIpcEndpoint.TryParseNamespacePid(status, 100, out int nsPid);
            Assert.False(result);
            Assert.Equal(100, nsPid);
        }

        [Fact]
        public void ParseTmpDir_TmpdirSet_ReturnsValue()
        {
            byte[] environ = Encoding.UTF8.GetBytes("PATH=/usr/bin\0TMPDIR=/custom/tmp\0HOME=/root\0");
            string result = PidIpcEndpoint.ParseTmpDir(environ);
            Assert.Equal("/custom/tmp", result);
        }

        [Fact]
        public void ParseTmpDir_TmpdirNotSet_ReturnsFallback()
        {
            byte[] environ = Encoding.UTF8.GetBytes("PATH=/usr/bin\0HOME=/root\0");
            string result = PidIpcEndpoint.ParseTmpDir(environ);
            Assert.Equal(Path.GetTempPath(), result);
        }

        [Fact]
        public void ParseTmpDir_EmptyEnviron_ReturnsFallback()
        {
            byte[] environ = new byte[0];
            string result = PidIpcEndpoint.ParseTmpDir(environ);
            Assert.Equal(Path.GetTempPath(), result);
        }

        #endregion

        #region Behavioral tests (platform-specific, real system calls)

        [ConditionalFact(nameof(IsLinux))]
        public void TryGetNamespacePid_CurrentProcess_SameNamespace_ReturnsFalse()
        {
            int currentPid = Process.GetCurrentProcess().Id;
            bool result = PidIpcEndpoint.TryGetNamespacePid(currentPid, out int nsPid);
            Assert.False(result);
            Assert.Equal(currentPid, nsPid);
        }

        [ConditionalFact(nameof(IsNotLinux))]
        public void TryGetNamespacePid_NonLinux_ReturnsFalse()
        {
            bool result = PidIpcEndpoint.TryGetNamespacePid(1, out int nsPid);
            Assert.False(result);
            Assert.Equal(1, nsPid);
        }

        [ConditionalFact(nameof(IsLinux))]
        public void TryGetNamespacePid_NonExistentPid_DoesNotThrow()
        {
            bool result = PidIpcEndpoint.TryGetNamespacePid(int.MaxValue, out int nsPid);
            Assert.False(result);
            Assert.Equal(int.MaxValue, nsPid);
        }

        [ConditionalFact(nameof(IsLinux))]
        public void GetProcessTmpDir_ChildProcess_ReadsTmpdir()
        {
            string customTmpDir = "/custom/tmp/test";
            ProcessStartInfo psi = new("sleep", "30")
            {
                UseShellExecute = false,
            };
            psi.Environment["TMPDIR"] = customTmpDir;

            using Process child = Process.Start(psi);
            try
            {
                string environPath = $"/proc/{child.Id}/environ";

                // Wait for /proc/{pid}/environ to be populated.
                // Between fork() and execve(), the environ file may be empty (0 bytes).
                bool environPopulated = false;
                const int MAX_TRIES = 20;
                const int DELAY_MS = 50;
                for (int attempt = 0; attempt < MAX_TRIES && !environPopulated; attempt++)
                {
                    try
                    {
                        environPopulated = File.ReadAllBytes(environPath).Length > 0;
                    }
                    catch (IOException)
                    {
                        // File may be temporarily unavailable.
                    }

                    if (!environPopulated)
                    {
                        Thread.Sleep(DELAY_MS);
                    }
                }

                // Read the child's environ directly for diagnostics
                string environPerms = "unknown";
                try
                {
                    environPerms = File.GetUnixFileMode(environPath).ToString();
                }
                catch (Exception ex)
                {
                    environPerms = $"error: {ex.GetType().Name}: {ex.Message}";
                }

                byte[] rawEnviron = Array.Empty<byte>();
                string environContent = string.Empty;
                string[] envVars = Array.Empty<string>();
                string tmpdirEntry = null;
                string environReadError = null;
                try
                {
                    rawEnviron = File.ReadAllBytes(environPath);
                    environContent = Encoding.UTF8.GetString(rawEnviron);
                    envVars = environContent.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                    tmpdirEntry = Array.Find(envVars, v => v.StartsWith("TMPDIR=", StringComparison.Ordinal));
                }
                catch (Exception ex)
                {
                    environReadError = $"{ex.GetType().Name}: {ex.Message}";
                }

                string diagnostics = $"Child PID: {child.Id}, "
                    + $"child exited: {child.HasExited}, "
                    + $"environ path: {environPath}, "
                    + $"environ permissions: {environPerms}, "
                    + $"parent TMPDIR: '{Environment.GetEnvironmentVariable("TMPDIR") ?? "(not set)"}', "
                    + $"psi.Environment TMPDIR: '{psi.Environment["TMPDIR"]}', "
                    + $"current user: {Environment.UserName}, "
                    + $"environ populated after poll: {environPopulated}, ";

                if (environReadError != null)
                {
                    diagnostics += $"environ read error: {environReadError}";
                }
                else
                {
                    diagnostics += $"environ size: {rawEnviron.Length} bytes, "
                        + $"env var count: {envVars.Length}, "
                        + $"TMPDIR entry: '{tmpdirEntry ?? "(not found)"}', "
                        + $"first 200 chars of environ: '{(environContent.Length > 200 ? environContent.Substring(0, 200) : environContent).Replace('\0', '|')}'";
                }

                string result;
                bool environReadable;
                try
                {
                    result = PidIpcEndpoint.GetProcessTmpDir(child.Id, out environReadable);
                }
                catch (Exception ex)
                {
                    Assert.Fail($"GetProcessTmpDir threw {ex.GetType().Name}: {ex.Message}. {diagnostics}");
                    return;
                }

                if (environReadable)
                {
                    Assert.True(result == customTmpDir,
                        $"Expected '{customTmpDir}' but got '{result}'. {diagnostics}");
                }
                else
                {
                    Assert.True(result == Path.GetTempPath(),
                        $"environ was not readable; expected fallback '{Path.GetTempPath()}' but got '{result}'. {diagnostics}");
                }
            }
            finally
            {
                child.Kill();
                child.WaitForExit();
            }
        }

        [Fact]
        public void GetDefaultAddress_NonExistentPid_ThrowsServerNotAvailable()
        {
            ServerNotAvailableException ex = Assert.Throws<ServerNotAvailableException>(
                () => PidIpcEndpoint.GetDefaultAddress(int.MaxValue));
            Assert.Contains("is not running", ex.Message);
        }

        [ConditionalFact(nameof(IsLinux))]
        public void GetProcessTmpDir_KernelThread_DoesNotThrow()
        {
            int unreadableEnvironPid = -1;
            foreach (string procEntry in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(procEntry), out int pid))
                {
                    continue;
                }

                try
                {
                    File.ReadAllBytes($"/proc/{pid}/environ");
                }
                catch (IOException)
                {
                    unreadableEnvironPid = pid;
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    // Another user's process is a different code path; keep looking.
                }
            }

            if (unreadableEnvironPid == -1)
            {
                Assert.Skip("No process with an unreadable (IOException) /proc/{pid}/environ was found.");
            }

            bool environReadable = true;
            string result = PidIpcEndpoint.GetProcessTmpDir(unreadableEnvironPid, out environReadable);

            Assert.False(environReadable, $"environ for PID {unreadableEnvironPid} was unexpectedly reported as readable.");
            Assert.Equal(Path.GetTempPath(), result);
        }

        #endregion
    }
}
