// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Diagnostics.NETCore.Client;
using Xunit.Extensions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class ProcessEnvironmentTests
    {
        private readonly ITestOutputHelper output;

        public ProcessEnvironmentTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        /// <summary>
        /// A simple test that collects process environment.
        /// </summary>
        [Fact]
        public void BasicEnvTest()
        {
            // as the attribute says, this test requires 5.0-rc1 or newer.  This has been tested locally on
            // an rc1 build and passes.  It is equivalent to the dotnet/runtime version of this test.
            using TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(targetFramework: "net5.0"), output);
            string testKey = "FOO";
            string testVal = "BAR";
            runner.AddEnvVar(testKey, testVal);
            runner.Start(3000);
            DiagnosticsClient client = new DiagnosticsClient(runner.Pid);
            Dictionary<string,string> env = client.GetProcessEnvironment();

            Assert.True(env.ContainsKey(testKey) && env[testKey].Equals(testVal));

            runner.Stop();
        }
    }
}
