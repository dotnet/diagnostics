// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.Tools.Stack
{
    public class StackTests
    {
        private readonly ITestOutputHelper output;

        private readonly string correctStack = @"  [Native Frames]
  System.Console!System.IO.StdInReader.ReadKey(bool&)
  System.Console!System.IO.SyncTextReader.ReadKey(bool&)
  System.Console!System.ConsolePal.ReadKey(bool)
  System.Console!System.Console.ReadKey()
  StackTracee!Tracee.Program.Main(class System.String[])";

        public StackTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Theory]
        [InlineData("netcoreapp3.1")]
        [InlineData("net5.0")]
        public async Task ReportsStacksCorrectly(string traceeFramework)
        {
            Command reportCommand = ReportCommandHandler.ReportCommand();

            var console = new TestConsole();
            var parser = new Parser(reportCommand);

            using TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(traceeName: "StackTracee", targetFramework: traceeFramework), output);
            runner.Start();

            // Wait for tracee to get to readkey call
            await Task.Delay(TimeSpan.FromSeconds(1));

            await parser.InvokeAsync($"report -p {runner.Pid}", console);

            string report = console.Out.ToString();

            output.WriteLine($"REPORT_START\n{report}REPORT_END");
            Assert.True(!string.IsNullOrEmpty(report));


            string[] correctStackParts = correctStack.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            string[] stackParts = report.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            int partIdx = 0;
            while (stackParts[partIdx].StartsWith("#") || stackParts[partIdx].StartsWith("Thread") || stackParts[partIdx].StartsWith("Found"))
                partIdx++;

            Assert.True(stackParts.Length - partIdx == correctStackParts.Length, $"{stackParts.Length - partIdx} != {correctStackParts.Length}");

            for (int i = partIdx, j = 0; i < stackParts.Length && j < correctStackParts.Length; i++, j++)
            {
                Assert.True(correctStackParts[j] == stackParts[i], $"{correctStackParts[j]} != {stackParts[i]}");
            }
        }
    }
}