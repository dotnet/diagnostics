// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.CommonTestRunner;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class PidIpcEndpointTests
    {
        private readonly ITestOutputHelper _output;

        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsNotLinux => !IsLinux;
        public static bool IsUnix => IsLinux || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public static IEnumerable<object[]> SpoofConfigurations
        {
            get
            {
                object[] config = TestRunner.Configurations.FirstOrDefault(
                    candidate => ((TestConfiguration)candidate[0]).RuntimeFrameworkVersionMajor == 10)
                    ?? TestRunner.Configurations.First();

                return new[]
                {
                    new object[] { config[0], false, false },
                    new object[] { config[0], false, true },
                    new object[] { config[0], true, false },
                    new object[] { config[0], true, true }
                };
            }
        }

        public PidIpcEndpointTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(Architecture.X64, 17)]
        [InlineData(Architecture.X86, 17)]
        [InlineData(Architecture.Arm, 17)]
        [InlineData(Architecture.Arm64, 17)]
        [InlineData((Architecture)5, 17)] // S390x
        [InlineData((Architecture)6, 17)] // LoongArch64
        [InlineData((Architecture)7, 17)] // Armv6
        [InlineData((Architecture)8, 21)] // Ppc64le
        [InlineData((Architecture)9, 17)] // RiscV64
        public void GetLinuxPeerCredentialsOption_ReturnsArchitectureValue(Architecture architecture, int expected)
        {
            Assert.Equal(expected, UnixSocketPeer.GetLinuxPeerCredentialsOption(architecture));
        }

        [Fact]
        public void GetLinuxPeerCredentialsOption_UnsupportedArchitecture_Throws()
        {
            Assert.Throws<PlatformNotSupportedException>(
                () => UnixSocketPeer.GetLinuxPeerCredentialsOption((Architecture)int.MaxValue));
        }

        [ConditionalFact(nameof(IsUnix))]
        public void IsConnectedToProcess_ValidatesKernelPeerCredentials()
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                string path = Path.Combine(directory, "peer-socket");
                using Socket listener = BindSocket(path);
                listener.Listen(1);

                using Socket client = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                client.Connect(new UnixDomainSocketEndPoint(path));
                using Socket server = listener.Accept();

                int pid = Environment.ProcessId;
                Assert.True(UnixSocketPeer.IsConnectedToProcess(client, pid));
                Assert.True(UnixSocketPeer.IsConnectedToProcess(server, pid));
                Assert.False(UnixSocketPeer.IsConnectedToProcess(client, pid + 1));
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
                using Socket router = BindSocket(Path.Combine(directory, $"dotnet-diagnostic-dsrouter-{Environment.ProcessId}-1-socket"));
                Assert.False(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out string address));
                Assert.Null(address);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalTheory(nameof(IsUnix))]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("invalid\0directory")]
        public void TryResolveAddress_InvalidDirectory_ReturnsFalse(string directory)
        {
            Assert.False(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out string address));
            Assert.Null(address);
        }

        [ConditionalFact(nameof(IsUnix))]
        public void TryResolveAddress_PreservesRouterTimestampSelection()
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                string runtimePath = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-1-socket");
                string routerPath = Path.Combine(directory, $"dotnet-diagnostic-dsrouter-{Environment.ProcessId}-1-socket");
                File.WriteAllText(runtimePath, "");
                Thread.Sleep(1100);
                File.WriteAllText(routerPath, "");
                Assert.True(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out string address));
                Assert.Equal(routerPath, address);

                File.Delete(runtimePath);
                Thread.Sleep(1100);
                File.WriteAllText(runtimePath, "");
                Assert.True(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out address));
                Assert.Equal(runtimePath, address);

                DateTime timestamp = DateTime.UtcNow;
                File.SetLastWriteTimeUtc(runtimePath, timestamp);
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
        public void TryResolveAddress_IgnoresInvalidCandidateName()
        {
            string directory = CreateSocketTestDirectory();
            try
            {
                string validPath = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-1-socket");
                string invalidPath = Path.Combine(directory, $"dotnet-diagnostic-{Environment.ProcessId}-2,x-socket");
                File.WriteAllText(validPath, "");
                Thread.Sleep(1100);
                File.WriteAllText(invalidPath, "");

                Assert.True(PidIpcEndpoint.TryResolveAddress(directory, Environment.ProcessId, out string address));
                Assert.Equal(validPath, address);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [ConditionalTheory(nameof(IsUnix))]
        [MemberData(nameof(SpoofConfigurations))]
        public async Task Connect_RejectsSpoofedCandidateAndFallsBack(TestConfiguration config, bool useRouterName, bool useAsync)
        {
            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            await runner.Start(testProcessTimeout: 60_000);

            string otherProcessAddress = PidIpcEndpoint.GetDefaultAddress(runner.Pid);
            string prefix = useRouterName ? "dotnet-diagnostic-dsrouter" : "dotnet-diagnostic";
            string spoofPath = Path.Combine(
                PidIpcEndpoint.IpcRootPath,
                $"{prefix}-{Environment.ProcessId}-99999999-socket");
            string validRouterPath = useRouterName
                ? Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-dsrouter-{Environment.ProcessId}-99999995-socket")
                : null;
            try
            {
                if (validRouterPath is not null)
                {
                    File.CreateSymbolicLink(validRouterPath, PidIpcEndpoint.GetDefaultAddress(Environment.ProcessId));
                    Thread.Sleep(1100);
                }
                File.CreateSymbolicLink(spoofPath, otherProcessAddress);

                Assert.True(PidIpcEndpoint.TryResolveAddress(
                    PidIpcEndpoint.IpcRootPath,
                    Environment.ProcessId,
                    out string resolvedAddress));
                Assert.Equal(spoofPath, resolvedAddress);

                DiagnosticsClient client = new(Environment.ProcessId);
                DiagnosticsClientApiShim clientShim = new(client, useAsync);
                ProcessInfo info = await clientShim.GetProcessInfo();
                Assert.Equal((ulong)Environment.ProcessId, info.ProcessId);
            }
            finally
            {
                File.Delete(spoofPath);
                if (validRouterPath is not null)
                {
                    File.Delete(validRouterPath);
                }
            }
        }

        [ConditionalTheory(nameof(IsUnix))]
        [InlineData(false)]
        [InlineData(true)]
        public void Connect_SkipsUnconnectableCandidate(bool useRouterName)
        {
            string prefix = useRouterName ? "dotnet-diagnostic-dsrouter" : "dotnet-diagnostic";
            string invalidPath = Path.Combine(
                PidIpcEndpoint.IpcRootPath,
                $"{prefix}-{Environment.ProcessId}-99999998-socket");
            string validRouterPath = useRouterName
                ? Path.Combine(PidIpcEndpoint.IpcRootPath, $"dotnet-diagnostic-dsrouter-{Environment.ProcessId}-99999995-socket")
                : null;
            try
            {
                if (validRouterPath is not null)
                {
                    File.CreateSymbolicLink(validRouterPath, PidIpcEndpoint.GetDefaultAddress(Environment.ProcessId));
                }
                File.WriteAllText(invalidPath, "");
                File.SetLastWriteTimeUtc(invalidPath, DateTime.UtcNow.AddMinutes(1));

                Assert.True(PidIpcEndpoint.TryResolveAddress(
                    PidIpcEndpoint.IpcRootPath,
                    Environment.ProcessId,
                    out string resolvedAddress));
                Assert.Equal(invalidPath, resolvedAddress);

                DiagnosticsClient client = new(Environment.ProcessId);
                ProcessInfo info = client.GetProcessInfo();
                Assert.Equal((ulong)Environment.ProcessId, info.ProcessId);
            }
            finally
            {
                File.Delete(invalidPath);
                if (validRouterPath is not null)
                {
                    File.Delete(validRouterPath);
                }
            }
        }

        [ConditionalFact(nameof(IsUnix))]
        public void Connect_SkipsOverlongCandidate()
        {
            string overlongPath = Path.Combine(
                PidIpcEndpoint.IpcRootPath,
                $"dotnet-diagnostic-{Environment.ProcessId}-{new string('9', 120)}-socket");
            File.WriteAllText(overlongPath, "");

            try
            {
                Assert.True(PidIpcEndpoint.TryResolveAddress(
                    PidIpcEndpoint.IpcRootPath,
                    Environment.ProcessId,
                    out string resolvedAddress));
                Assert.Equal(overlongPath, resolvedAddress);

                DiagnosticsClient client = new(Environment.ProcessId);
                ProcessInfo info = client.GetProcessInfo();
                Assert.Equal((ulong)Environment.ProcessId, info.ProcessId);
            }
            finally
            {
                File.Delete(overlongPath);
            }
        }

        [ConditionalTheory(nameof(IsUnix))]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Connect_PreferredRouterUnavailable_DoesNotFallBackToRuntime(bool useAsync)
        {
            DiagnosticsClientApiShim client = new(new DiagnosticsClient(Environment.ProcessId), useAsync);
            ProcessInfo baseline = await client.GetProcessInfo(TimeSpan.FromSeconds(5));
            Assert.Equal((ulong)Environment.ProcessId, baseline.ProcessId);

            string routerPath = Path.Combine(
                PidIpcEndpoint.IpcRootPath,
                $"dotnet-diagnostic-dsrouter-{Environment.ProcessId}-99999996-socket");
            File.WriteAllText(routerPath, "");
            try
            {
                File.SetLastWriteTimeUtc(routerPath, DateTime.UtcNow.AddMinutes(1));
                Assert.True(PidIpcEndpoint.TryResolveAddress(
                    PidIpcEndpoint.IpcRootPath,
                    Environment.ProcessId,
                    out string selectedAddress));
                Assert.Equal(routerPath, selectedAddress);

                await Assert.ThrowsAsync<ServerNotAvailableException>(async () => {
                    ProcessInfo info = await client.GetProcessInfo(TimeSpan.FromSeconds(5));
                    _output.WriteLine($"Unavailable router endpoint returned runtime ProcessInfo for PID {info.ProcessId}.");
                });
            }
            finally
            {
                File.Delete(routerPath);
            }

            ProcessInfo recovered = await client.GetProcessInfo(TimeSpan.FromSeconds(5));
            Assert.Equal((ulong)Environment.ProcessId, recovered.ProcessId);
        }

        [ConditionalFact(nameof(IsUnix))]
        public async Task ConnectAsync_AlreadyCanceled_ThrowsOperationCanceled()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            PidIpcEndpoint endpoint = new(Environment.ProcessId);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => endpoint.ConnectAsync(cancellation.Token));
        }

        [Fact]
        public void GetProcessInfo_CurrentProcess_ConnectsThroughDefaultEndpoint()
        {
            DiagnosticsClient client = new(Environment.ProcessId);
            ProcessInfo info = client.GetProcessInfo();
            Assert.Equal((ulong)Environment.ProcessId, info.ProcessId);
            Assert.Contains(Environment.ProcessId, DiagnosticsClient.GetPublishedProcesses());
        }

        [ConditionalFact(nameof(IsLinux))]
        public async Task GetPublishedProcesses_EmptyTargetTmpdir_FindsTarget()
        {
            TestConfiguration config = (TestConfiguration)SpoofConfigurations.First()[0];
            DebuggeeConfiguration debuggee = await DebuggeeCompiler.Execute(config, "Tracee", _output);
            bool useHost = !string.IsNullOrWhiteSpace(config.HostExe);
            string executable = useHost ? config.HostExe : debuggee.BinaryExePath;
            string arguments = useHost ? $"{config.HostArgs} \"{debuggee.BinaryExePath}\" unused-pipe" : "unused-pipe";
            ProcessStartInfo psi = new(executable, arguments) { UseShellExecute = false };
            psi.Environment["DOTNET_ROOT"] = config.DotNetRoot;
            psi.Environment["TMPDIR"] = string.Empty;

            using Process child = Process.Start(psi);
            string socketPath = null;
            try
            {
                bool found = false;
                for (int attempt = 0; attempt < 100 && !found; attempt++)
                {
                    found = PidIpcEndpoint.TryResolveAddress("/tmp/", child.Id, out socketPath);
                    if (!found)
                    {
                        await Task.Delay(100);
                    }
                }

                Assert.True(found, "Target diagnostic socket was not created.");
                Assert.Contains(child.Id, DiagnosticsClient.GetPublishedProcesses());
                Assert.Equal((ulong)child.Id, new DiagnosticsClient(child.Id).GetProcessInfo().ProcessId);
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill();
                    child.WaitForExit();
                }
                if (socketPath != null)
                {
                    File.Delete(socketPath);
                }
            }
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
            Assert.Equal("/tmp/", result);
        }

        [Fact]
        public void ParseTmpDir_EmptyTmpdir_ReturnsFallback()
        {
            byte[] environ = Encoding.UTF8.GetBytes("TMPDIR=\0");
            string result = PidIpcEndpoint.ParseTmpDir(environ);
            Assert.Equal("/tmp/", result);
        }

        [Fact]
        public void ParseTmpDir_EmptyEnviron_ReturnsFallback()
        {
            byte[] environ = new byte[0];
            string result = PidIpcEndpoint.ParseTmpDir(environ);
            Assert.Equal("/tmp/", result);
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
        public async Task GetProcessTmpDir_ChildProcess_ReadsTmpdir()
        {
            string customTmpDir = "/custom/tmp/test";
            ProcessStartInfo psi = new("sh", "-c \"echo ready; read -r line\"")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };
            psi.Environment["TMPDIR"] = customTmpDir;

            using Process child = Process.Start(psi);
            try
            {
                string environPath = $"/proc/{child.Id}/environ";

                // A nonempty environ can still be inherited from the parent before exec.
                // Wait for the child to run, then keep it alive with its redirected stdin.
                using CancellationTokenSource startupTimeout = new(TimeSpan.FromSeconds(30));
                Assert.Equal("ready", await child.StandardOutput.ReadLineAsync(startupTimeout.Token));

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
                    + $"current user: {Environment.UserName}, ";

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
