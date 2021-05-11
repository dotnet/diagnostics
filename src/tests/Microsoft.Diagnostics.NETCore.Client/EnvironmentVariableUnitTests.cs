using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client.UnitTests
{
    public class EnvironmentVariableUnitTests
    {
        private readonly ITestOutputHelper output;

        public EnvironmentVariableUnitTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Fact]
        public void GetEnvironmentVariable()
        {
            Dictionary<string, string> envVars = new Dictionary<string, string>
            {
                { "TestEmpty", "" },
                { "TestExists", "Hi!" }
            };
            using TestRunner runner = new TestRunner(testExePath: CommonHelper.GetTraceePathWithArgs(), 
                                                     _outputHelper: output,
                                                     envVars: envVars);
            runner.Start(timeoutInMSPipeCreation: 15_000, testProcessTimeout: 60_000);
            DiagnosticsClient client = new DiagnosticsClient(runner.Pid);

            Assert.Null(client.GetEnvironmentVariable("TestNotExists"));
            Assert.Null(client.GetEnvironmentVariable("TestEmpty"));
            Assert.Equal(envVars["TestExists"], client.GetEnvironmentVariable("TestExists"));
        }

        [Fact]
        public void SetEnvironmentVariable()
        {
            Dictionary<string, string> envVars = new Dictionary<string, string>
            {
                { "TestOverwrite", "Original" },
                { "TestClear", "Hi!" }
            };
            using TestRunner runner = new TestRunner(testExePath: CommonHelper.GetTraceePathWithArgs(),
                                                     _outputHelper: output,
                                                     envVars: envVars);
            runner.Start(timeoutInMSPipeCreation: 15_000, testProcessTimeout: 60_000);
            DiagnosticsClient client = new DiagnosticsClient(runner.Pid);

            Assert.Equal(envVars["TestClear"], client.GetEnvironmentVariable("TestClear"));
            client.SetEnvironmentVariable("TestClear", null);
            Assert.Null(client.GetEnvironmentVariable("TestClear"));

            Assert.Equal(envVars["TestOverwrite"], client.GetEnvironmentVariable("TestOverwrite"));
            client.SetEnvironmentVariable("TestOverwrite", "NewValue");
            Assert.Equal("NewValue", client.GetEnvironmentVariable("TestOverwrite"));
        }
    }
}
