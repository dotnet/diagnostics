// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Microsoft.Diagnostics.Tools.Trace
{

    public class ChildProcessTests
    {
        // Pass ITestOutputHelper into the test class, which xunit provides per-test
        public ChildProcessTests(ITestOutputHelper outputHelper)
        {
            OutputHelper = outputHelper;
        }

        private ITestOutputHelper OutputHelper { get; }

        private void LaunchDotNetTrace(string command, out int exitCode, out string stdOut, out string stdErr)
        {
            string dotnetTracePathWithArgs = CommonHelper.GetTraceePathWithArgs(traceeName: "dotnet-trace").Replace("net5.0", "netcoreapp3.1");
            ProcessStartInfo startInfo = new ProcessStartInfo(CommonHelper.HostExe, $"{dotnetTracePathWithArgs} {command}");

            OutputHelper.WriteLine($"Launching: {startInfo.FileName} {startInfo.Arguments}");
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            using (Process process = Process.Start(startInfo))
            {
                const int processTimeout = 15000;
                bool processExitedCleanly = process.WaitForExit(processTimeout);
                if (!processExitedCleanly)
                {
                    OutputHelper.WriteLine($"Forced kill of process after {processTimeout}ms");
                    process.Kill();
                }

                OutputHelper.WriteLine("StdErr");
                stdErr = process.StandardError.ReadToEnd();
                OutputHelper.WriteLine(stdErr);
                OutputHelper.WriteLine("StdOut");
                stdOut = process.StandardOutput.ReadToEnd();
                OutputHelper.WriteLine(stdOut);

                Assert.True(processExitedCleanly, "Launched process failed to exit");
                exitCode = process.ExitCode;
            }
        }

        [Theory]
        [InlineData("232", 232)]
        [InlineData("0", 0)]
        public void VerifyExitCode(string commandLineArg, int exitCode)
        {
            string exitCodeTraceePath = CommonHelper.GetTraceePathWithArgs(traceeName: "ExitCodeTracee", targetFramework: "net5.0");

            LaunchDotNetTrace($"collect -o verifyexitcode.nettrace -- {CommonHelper.HostExe} {exitCodeTraceePath} {commandLineArg}", out int dotnetTraceExitCode, out string stdOut, out string stdErr);
            Assert.Equal(exitCode, dotnetTraceExitCode);

            Assert.Contains($"Process exited with code '{exitCode}'.", stdOut);
        }

        [Theory]
        [InlineData("0 this is a message", new string[] { "\nthis\n", "\nis\n", "\na\n" })]
        public void VerifyHideIO(string commandLineArg, string[] stringsInOutput)
        {
            string exitCodeTraceePath = CommonHelper.GetTraceePathWithArgs(traceeName: "ExitCodeTracee", targetFramework: "net5.0");

            LaunchDotNetTrace($"collect -o VerifyHideIO.nettrace -- {CommonHelper.HostExe} {exitCodeTraceePath} {commandLineArg}", out int dotnetTraceExitCode, out string stdOut, out string stdErr);
            Assert.Equal(0, dotnetTraceExitCode);
            stdOut = stdOut.Replace("\r", "");

            foreach (string s in stringsInOutput)
                Assert.DoesNotContain(s, stdOut);
        }

        [Theory]
        [InlineData("0 this is a message", new string[] { "\nthis\n", "\nis\n", "\na\n" })]
        public void VerifyShowIO(string commandLineArg, string[] stringsInOutput)
        {
            string exitCodeTraceePath = CommonHelper.GetTraceePathWithArgs(traceeName: "ExitCodeTracee", targetFramework: "net5.0");

            LaunchDotNetTrace($"collect -o VerifyShowIO.nettrace --show-child-io -- {CommonHelper.HostExe} {exitCodeTraceePath} {commandLineArg}", out int dotnetTraceExitCode, out string stdOut, out string stdErr);
            Assert.Equal(0, dotnetTraceExitCode);
            stdOut = stdOut.Replace("\r", "");

            foreach (string s in stringsInOutput)
                Assert.Contains(s, stdOut);
        }
    }
}
