// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace Microsoft.Diagnostics.Tools.Stack
{
    public class StackTests
    {
        private readonly ITestOutputHelper _output;

        private const string _correctStack70 = @"  [Native Frames]
  System.Console.il!Interop+Kernel32.ReadFile(int,unsigned int8*,int32,int32&,int)
  System.Console.il!System.ConsolePal+WindowsConsoleStream.ReadFileNative(int,value class System.Span`1<unsigned int8>,bool,int32&,bool)
  System.Console.il!System.ConsolePal+WindowsConsoleStream.Read(value class System.Span`1<unsigned int8>)
  System.Console.il!System.IO.ConsoleStream.Read(unsigned int8[],int32,int32)
  System.Private.CoreLib.il!System.IO.StreamReader.ReadBuffer()
  System.Private.CoreLib.il!System.IO.StreamReader.Read()
  System.Console.il!System.IO.SyncTextReader.Read()
  System.Console.il!System.Console.Read()
  ?!?";

        private const string _correctStack60 = @"  [Native Frames]
  System.Console.il!System.ConsolePal+WindowsConsoleStream.ReadFileNative(int,value class System.Span`1<unsigned int8>,bool,int32&,bool)
  System.Console.il!System.ConsolePal+WindowsConsoleStream.Read(value class System.Span`1<unsigned int8>)
  System.Console.il!System.IO.ConsoleStream.Read(unsigned int8[],int32,int32)
  System.Private.CoreLib.il!System.IO.StreamReader.ReadBuffer()
  System.Private.CoreLib.il!System.IO.StreamReader.Read()
  System.Console.il!System.IO.SyncTextReader.Read()
  System.Console.il!System.Console.Read()
  StackTracee!Tracee.Program.Main(class System.String[])";

        private const string _correctStack31 = @"  [Native Frames]
  System.Console.il!System.ConsolePal+WindowsConsoleStream.ReadFileNative(int,unsigned int8[],int32,int32,bool,int32&,bool)
  System.Console.il!System.ConsolePal+WindowsConsoleStream.Read(unsigned int8[],int32,int32)
  System.Private.CoreLib.il!System.IO.StreamReader.ReadBuffer()
  System.Private.CoreLib.il!System.IO.StreamReader.Read()
  System.Console.il!System.IO.SyncTextReader.Read()
  System.Console.il!System.Console.Read()
  StackTracee!Tracee.Program.Main(class System.String[])";

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public StackTests(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task ReportsStacksCorrectly(TestConfiguration config)
        {
            Command reportCommand = ReportCommandHandler.ReportCommand();

            var console = new TestConsole();
            var parser = new Parser(reportCommand);

            await using TestRunner runner = await TestRunner.Create(config, _output, "StackTracee", usePipe: false);
            await runner.Start();

            // Wait for tracee to get to readkey call
            await Task.Delay(TimeSpan.FromSeconds(1));

            await parser.InvokeAsync($"report -p {runner.Pid}", console);

            string report = console.Out.ToString();

            runner.WriteLine($"REPORT_START\n{report}REPORT_END");
            Assert.True(!string.IsNullOrEmpty(report));

            string correctStack = config.RuntimeFrameworkVersionMajor switch
            {
                7 => _correctStack70,
                6 => _correctStack60,
                3 => _correctStack31,
                _ => throw new NotSupportedException($"Runtime version {config.RuntimeFrameworkVersionMajor} not supported")
            };
            string[] correctStackParts = correctStack.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            string[] stackParts = report.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            int partIdx = 0;
            while (stackParts[partIdx].StartsWith("#") || stackParts[partIdx].StartsWith("Thread") || stackParts[partIdx].StartsWith("Found"))
            {
                partIdx++;
            }

            Assert.True(stackParts.Length - partIdx == correctStackParts.Length, $"{stackParts.Length - partIdx} != {correctStackParts.Length}");

            for (int i = partIdx, j = 0; i < stackParts.Length && j < correctStackParts.Length; i++, j++)
            {
                Assert.True(correctStackParts[j] == stackParts[i], $"{correctStackParts[j]} != {stackParts[i]}");
            }
        }
    }
}
