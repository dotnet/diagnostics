using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

using Microsoft.Diagnostics.TestHelpers;

namespace newtest
{
    public class UnitTest1
    {
        public string GetTraceePath()
        {
            var curPath = Directory.GetCurrentDirectory();
            var traceePath = curPath.Replace("newtest.UnitTests", "Tracee");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return traceePath + "\\Tracee.dll";
            }
            else
            {
                return traceePath + "/Tracee.dll";
            }
        }

        public string GetHostExePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var repoRoot = "..\\..\\..\\..\\..";
                var dotnetHostExe = "\\.dotnet\\dotnet.exe";
                return repoRoot + dotnetHostExe;
            }
            else
            {
                var repoRoot = "../../../../..";
                var dotnetHostExe = "/.dotnet/dotnet";
                return repoRoot + dotnetHostExe;
            }
        }

        [Fact]
        public void ProcessLaunchTest1()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(GetHostExePath(), GetTraceePath());
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            Process process = Process.Start(startInfo);
        
            Assert.True(process != null);
            Assert.False(process.HasExited);
        }
    }
}
