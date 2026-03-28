// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.DotNet.XUnitExtensions;
using Xunit;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class PidIpcEndpointTests
    {
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsNotLinux => !IsLinux;

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
                const long MAX_TRIES = 20;
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

        #endregion
    }
}
