using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

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
                return traceePath + "/Tracee.exe";
            }
            return traceePath + "/Tracee";
        }

        [Fact]
        public void ProcessLaunchTest1()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(GetTraceePath());
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            Process process = Process.Start(startInfo);
        
            Assert.True(process != null);
            Assert.False(process.HasExited);
        }
    }
}
