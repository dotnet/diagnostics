// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
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

        private readonly string correctNonWindowsStackText = @"[Native Frames]
  System.Private.CoreLib!System.Globalization.CompareInfo.IcuCompareString(value class System.ReadOnlySpan`1<wchar>,value class System.ReadOnlySpan`1<wchar>,value class System.Globalization.CompareOptions)
  System.Private.CoreLib!System.Globalization.CompareInfo.Compare(value class System.ReadOnlySpan`1<wchar>,value class System.ReadOnlySpan`1<wchar>,value class System.Globalization.CompareOptions)
  System.Private.CoreLib!System.Globalization.CompareInfo.Compare(class System.String,class System.String,value class System.Globalization.CompareOptions)
  System.Private.CoreLib!System.Globalization.TextInfo.PopulateIsAsciiCasingSameAsInvariant()
  System.Private.CoreLib!System.Globalization.TextInfo.ChangeCaseCommon(class System.String)
  System.Private.CoreLib!System.Globalization.TextInfo.ToLower(class System.String)
  System.Private.CoreLib!System.String.ToLowerInvariant()
  System.Console!System.Text.EncodingHelper.GetCharset()
  System.Console!System.Text.EncodingHelper.GetEncodingFromCharset()
  System.Console!System.ConsolePal.GetConsoleEncoding()
  System.Console!System.Console.get_OutputEncoding()
  System.Console!System.Console.CreateOutputWriter(class System.IO.Stream)
  System.Console!System.Console.<get_Out>g__EnsureInitialized|25_0()
  System.Console!System.Console.get_Out()
  System.Console!System.Console.WriteLine(class System.String,class System.Object)
  Tracee!Tracee.Program.Main(class System.String[])";

        private readonly string correctWindowsStackText = @"";

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

            using TestRunner runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(targetFramework: traceeFramework), output);
            runner.Start();

            await parser.InvokeAsync($"report -p {runner.Pid}", console);

            string[] correctStackParts = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ?
                correctWindowsStackText.Split(System.Environment.NewLine) :
                correctNonWindowsStackText.Split(System.Environment.NewLine);

            string[] stackParts = console.Out.ToString().Split(System.Environment.NewLine);

            for (int i = 0, j = 0; i < stackParts.Length; i++)
            {
                if (stackParts[i].StartsWith("#") || stackParts[i].StartsWith("Thread") || stackParts[i].StartsWith("Found"))
                    continue;
                Assert.Equal(correctStackParts[j++], stackParts[i]);
            }

            System.Console.WriteLine(console.Out.ToString());
        }
    }
}