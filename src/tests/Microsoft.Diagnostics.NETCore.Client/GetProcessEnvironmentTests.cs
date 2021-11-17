// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class ProcessEnvironmentTests
    {
        private readonly ITestOutputHelper output;

        public ProcessEnvironmentTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public Task BasicEnvTest()
        {
            return BasicEnvTestCore(useAsync: false);
        }

        [Fact]
        public Task BasicEnvTestAsync()
        {
            return BasicEnvTestCore(useAsync: true);
        }

        /// <summary>
        /// A simple test that collects process environment.
        /// </summary>
        private async Task BasicEnvTestCore(bool useAsync)
        {
            // as the attribute says, this test requires 5.0-rc1 or newer.  This has been tested locally on
            // an rc1 build and passes.  It is equivalent to the dotnet/runtime version of this test.
            using TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(targetFramework: "net5.0"), output);
            string testKey = "FOO";
            string testVal = "BAR";
            runner.AddEnvVar(testKey, testVal);
            runner.Start(timeoutInMSPipeCreation: 3000);
            var clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);
            Dictionary<string,string> env = await clientShim.GetProcessEnvironment();

            Assert.True(env.ContainsKey(testKey) && env[testKey].Equals(testVal));

            runner.Stop();
        }
    }
}
