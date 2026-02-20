// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

        [Fact]
        public void GetDiagnosticSocketSearchPattern_ReturnsExpectedFormat()
        {
            string pattern = PidIpcEndpoint.GetDiagnosticSocketSearchPattern(1);
            Assert.Equal("dotnet-diagnostic-1-*-socket", pattern);
        }

        [Fact]
        public void GetDiagnosticSocketSearchPattern_LargePid()
        {
            string pattern = PidIpcEndpoint.GetDiagnosticSocketSearchPattern(32768);
            Assert.Equal("dotnet-diagnostic-32768-*-socket", pattern);
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
                Environment = { ["TMPDIR"] = customTmpDir },
                UseShellExecute = false,
            };

            using Process child = Process.Start(psi);
            try
            {
                string result = PidIpcEndpoint.GetProcessTmpDir(child.Id);
                Assert.Equal(customTmpDir, result);
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
