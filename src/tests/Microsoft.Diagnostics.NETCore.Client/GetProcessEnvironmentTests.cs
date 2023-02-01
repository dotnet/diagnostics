// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.TestHelpers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825 

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class ProcessEnvironmentTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public ProcessEnvironmentTests(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task BasicEnvTest(TestConfiguration config)
        {
            return BasicEnvTestCore(config, useAsync: false);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public Task BasicEnvTestAsync(TestConfiguration config)
        {
            return BasicEnvTestCore(config, useAsync: true);
        }

        /// <summary>
        /// A simple test that collects process environment.
        /// </summary>
        private async Task BasicEnvTestCore(TestConfiguration config, bool useAsync)
        {
            if (config.RuntimeFrameworkVersionMajor < 5)
            {
                throw new SkipTestException("Not supported on < .NET 5.0");
            }
            // as the attribute says, this test requires 5.0-rc1 or newer.  This has been tested locally on
            // an rc1 build and passes.  It is equivalent to the dotnet/runtime version of this test.
            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            string testKey = "FOO";
            string testVal = "BAR";
            runner.AddEnvVar(testKey, testVal);
            await runner.Start();
            var clientShim = new DiagnosticsClientApiShim(new DiagnosticsClient(runner.Pid), useAsync);
            Dictionary<string,string> env = await clientShim.GetProcessEnvironment();

            Assert.True(env.ContainsKey(testKey) && env[testKey].Equals(testVal));

            runner.WakeupTracee();
        }
    }
}
