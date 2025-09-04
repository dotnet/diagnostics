// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.Diagnostics.CommonTestRunner;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class ChildProcessTests
    {
        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        // Pass ITestOutputHelper into the test class, which xunit provides per-test
        public ChildProcessTests(ITestOutputHelper outputHelper)
        {
            OutputHelper = outputHelper;
        }

        private ITestOutputHelper OutputHelper { get; }

        private void LaunchDotNetTrace(TestConfiguration config, string dotnetTraceCommand, string traceeArguments, out int exitCode, out string stdOut, out string stdErr)
        {
            if (config.RuntimeFrameworkVersionMajor < 5)
            {
                throw new SkipTestException("Not supported on < .NET 5.0");
            }
            DebuggeeConfiguration debuggeeConfig = DebuggeeCompiler.Execute(config, "ExitCodeTracee", OutputHelper).GetAwaiter().GetResult();

            StringBuilder dotnetTraceArguments = new();
            dotnetTraceArguments.Append(config.DotNetTracePath());
            dotnetTraceArguments.Append(' ');
            dotnetTraceArguments.Append(dotnetTraceCommand);
            dotnetTraceArguments.Append(" -- ");

            if (!string.IsNullOrWhiteSpace(config.HostExe))
            {
                dotnetTraceArguments.Append(config.HostExe);
                dotnetTraceArguments.Append(' ');
                if (!string.IsNullOrWhiteSpace(config.HostArgs))
                {
                    dotnetTraceArguments.Append(config.HostArgs);
                    dotnetTraceArguments.Append(' ');
                }
            }
            dotnetTraceArguments.Append(debuggeeConfig.BinaryExePath);
            dotnetTraceArguments.Append(' ');
            dotnetTraceArguments.Append(traceeArguments);

            ProcessStartInfo startInfo = new(config.DotNetTraceHost(), dotnetTraceArguments.ToString());

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

            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                OutputHelper.WriteLine(stdErr);
            }
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public void VerifyExitCode(TestConfiguration config)
        {
            VerifyExitCodeX(config, "232", 232);
            VerifyExitCodeX(config, "0", 0);
        }

        private void VerifyExitCodeX(TestConfiguration config, string commandLineArg, int exitCode)
        {
            LaunchDotNetTrace(config, "collect -o verifyexitcode.nettrace", commandLineArg, out int dotnetTraceExitCode, out string stdOut, out string stdErr);
            Assert.Equal(exitCode, dotnetTraceExitCode);
            Assert.Contains($"Process exited with code '{exitCode}'.", stdOut);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public void VerifyHideIO(TestConfiguration config)
        {
            LaunchDotNetTrace(config, "collect -o VerifyHideIO.nettrace", "0 this is a message", out int dotnetTraceExitCode, out string stdOut, out string stdErr);
            Assert.Equal(0, dotnetTraceExitCode);
            stdOut = stdOut.Replace("\r", "");

            string[] stringsInOutput = new string[] { "\nthis\n", "\nis\n", "\na\n" };
            foreach (string s in stringsInOutput)
            {
                Assert.DoesNotContain(s, stdOut);
            }
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public void VerifyShowIO(TestConfiguration config)
        {
            LaunchDotNetTrace(config, "collect -o VerifyShowIO.nettrace --show-child-io", "0 this is a message", out int dotnetTraceExitCode, out string stdOut, out string stdErr);
            Assert.Equal(0, dotnetTraceExitCode);
            stdOut = stdOut.Replace("\r", "");

            string[] stringsInOutput = new string[] { "\nthis\n", "\nis\n", "\na\n" };
            foreach (string s in stringsInOutput)
            {
                Assert.Contains(s, stdOut);
            }
        }
    }
}
