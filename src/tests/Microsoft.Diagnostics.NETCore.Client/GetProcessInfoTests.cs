// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class GetProcessInfoTests
    {
        private readonly ITestOutputHelper output;

        public GetProcessInfoTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public Task BasicProcessInfoTest()
        {
            return BasicProcessInfoTestCore(useAsync: false);
        }

        [Fact]
        public Task BasicProcessInfoTestAsync()
        {
            return BasicProcessInfoTestCore(useAsync: true);
        }

        private async Task BasicProcessInfoTestCore(bool useAsync)
        {
            using TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(targetFramework: "net5.0"), output);
            runner.Start();

            try
            {
                DiagnosticsClientApiShim clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);

                ProcessInfo processInfo = await clientShim.GetProcessInfo();

                Assert.NotNull(processInfo);
                Assert.Equal(runner.Pid, (int)processInfo.ProcessId);
                Assert.NotNull(processInfo.CommandLine);
                Assert.NotNull(processInfo.OperatingSystem);
                Assert.NotNull(processInfo.ProcessArchitecture);
                //Assert.Equal("Tracee", processInfo.ManagedEntrypointAssemblyName);
                //Version clrVersion = ParseVersionRemoveLabel(processInfo.ClrProductVersionString);
                //Assert.True(clrVersion >= new Version(6, 0, 0));
            }
            finally
            {
                runner.PrintStatus();
            }
        }

        private static Version ParseVersionRemoveLabel(string versionString)
        {
            Assert.NotNull(versionString);
            int prereleaseLabelIndex = versionString.IndexOf('-');
            if (prereleaseLabelIndex >= 0)
            {
                versionString = versionString.Substring(0, prereleaseLabelIndex);
            }
            return Version.Parse(versionString);
        }
    }
}
