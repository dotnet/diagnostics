// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tests.Common;
using Microsoft.Diagnostics.Tools.Trace;
using Xunit;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class CollectCommandFunctionalTests
    {
        private const string ExpectedPayload = "CollectCommandFunctionalTestsTraceData";

        public sealed record CollectArgs(
            FileInfo output,
            CancellationToken ct = default,
            CommandLineConfiguration cliConfig = null,
            int processId = -1,
            uint buffersize = 1,
            string providers = "",
            string profile = "",
            int formatValue = (int)TraceFileFormat.NetTrace,
            TimeSpan duration = default,
            string clrevents = "",
            string clreventlevel = "",
            string name = "",
            string diagnosticPort = "",
            bool showchildio = false,
            bool resumeRuntime = false,
            string stoppingEventProviderName = "",
            string stoppingEventEventName = "",
            string stoppingEventPayloadFilter = "",
            bool? rundown = false,
            string dsrouter = "")
        {
            internal TraceFileFormat Format => (TraceFileFormat)formatValue;
            public string OutputFilePath => output.FullName;
            public int ProcessId => processId == -1 ? Environment.ProcessId : processId;
        }

        [Theory]
        [MemberData(nameof(BasicCases))]
        public async Task CollectCommandProviderConfigurationConsolidation(CollectArgs args, string[] expectedSubset)
        {
            MockConsole console = new(600, 400);
            string[] rawLines = await RunAsync(args, console).ConfigureAwait(true);
            console.AssertSanitizedLinesEqual(CollectSanitizer, expectedSubset);

            Assert.True(File.Exists(args.OutputFilePath), $"Trace output file not found: {args.OutputFilePath}");
            byte[] actual = File.ReadAllBytes(args.OutputFilePath);
            byte[] expected = Encoding.UTF8.GetBytes(ExpectedPayload);
            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected, actual);
        }

        private static async Task<string[]> RunAsync(CollectArgs config, MockConsole console)
        {
            // Override CollectCommand seams for testing
            CollectCommandHandler.StartTraceSessionAsync = (client, cfg, ct) => Task.FromResult<CollectCommandHandler.ICollectSession>(new TestCollectSession());
            CollectCommandHandler.ResumeRuntimeAsync = (client, ct) => Task.CompletedTask;
            CollectCommandHandler.IsOutputRedirected = () => false;
            CollectCommandHandler.AreCursorOperationsSupported = () => false;

            Console.SetOut(console);
            int exit = await CollectCommandHandler.Collect(
                config.ct,
                config.cliConfig,
                config.ProcessId,
                config.output,
                config.buffersize,
                config.providers,
                config.profile,
                config.Format,
                config.duration,
                config.clrevents,
                config.clreventlevel,
                config.name,
                config.diagnosticPort,
                config.showchildio,
                config.resumeRuntime,
                config.stoppingEventProviderName,
                config.stoppingEventEventName,
                config.stoppingEventPayloadFilter,
                config.rundown,
                config.dsrouter
            ).ConfigureAwait(true);
            if (exit != 0)
            {
                throw new InvalidOperationException($"Collect exited with return code {exit}.");
            }
            Console.SetOut(Console.Out);
            return console.Lines;
        }

        private static string[] CollectSanitizer(string[] lines)
        {
            List<string> result = new();
            bool traceStatusSeen = false;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                if (line.StartsWith("Process        :", StringComparison.Ordinal))
                {
                    result.Add("Process : <PROCESS>");
                }
                else if (line.StartsWith("Output File    :", StringComparison.Ordinal))
                {
                    result.Add("Output File : <FILE>");
                }
                else if (line.Contains("Recording trace", StringComparison.Ordinal) || line.Contains("Press <Enter>", StringComparison.Ordinal))
                {
                    if (!traceStatusSeen)
                    {
                        traceStatusSeen = true;
                        result.Add(line);
                    }
                }
                else
                {
                    string sanitizedLine = string.Join(" ", line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                    result.Add(sanitizedLine);
                }
            }
            return result.ToArray();
        }

        private sealed class TestCollectSession : CollectCommandHandler.ICollectSession
        {
            private static readonly byte[] _testBytes = Encoding.UTF8.GetBytes(ExpectedPayload);
            private readonly MemoryStream _stream = new(_testBytes);
            public Stream EventStream => _stream;
            public void Stop() { _ = _stream; }
            public void Dispose() => _stream.Dispose();
        }

        public static IEnumerable<object[]> BasicCases()
        {
            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("noProviders")),
                ExpectProvidersWithMessages(
                    new[]
                    {
                        "No profile or providers specified, defaulting to trace profile 'cpu-sampling'"
                    },
                    "Microsoft-DotNETCore-SampleProfiler 0x0000F00000000000 Informational(4) --profile",
                    "Microsoft-Windows-DotNETRuntime 0x00000014C14FCCBD Informational(4) --profile")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("singleProvider"), providers: "Foo:0x1:4"),
                ExpectProviders(
                    "Foo 0x0000000000000001 Informational(4) --providers")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("multipleProviders"), providers: "Foo:0x1:4,Bar:0x2:4"),
                ExpectProviders(
                    "Foo 0x0000000000000001 Informational(4) --providers",
                    "Bar 0x0000000000000002 Informational(4) --providers")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("profileOnly"), profile: "cpu-sampling"),
                ExpectProviders(
                    "Microsoft-DotNETCore-SampleProfiler 0x0000F00000000000 Informational(4) --profile",
                    "Microsoft-Windows-DotNETRuntime 0x00000014C14FCCBD Informational(4) --profile")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("profileAndProviders"), profile: "cpu-sampling", providers: "Foo:0x1:4"),
                ExpectProviders(
                    "Foo 0x0000000000000001 Informational(4) --providers",
                    "Microsoft-DotNETCore-SampleProfiler 0x0000F00000000000 Informational(4) --profile",
                    "Microsoft-Windows-DotNETRuntime 0x00000014C14FCCBD Informational(4) --profile")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("profileAndClrEventsIgnored"), profile: "cpu-sampling", clrevents: "gc"),
                ExpectProvidersWithMessages(
                    new[]
                    {
                        "The argument --clrevents gc will be ignored because the CLR provider was configured via either --profile or --providers command."
                    },
                    "Microsoft-DotNETCore-SampleProfiler 0x0000F00000000000 Informational(4) --profile",
                    "Microsoft-Windows-DotNETRuntime 0x00000014C14FCCBD Informational(4) --profile")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("profileOverriddenRuntime"), profile: "cpu-sampling", providers: "Microsoft-Windows-DotNETRuntime:0x1:4"),
                ExpectProviders(
                    "Microsoft-Windows-DotNETRuntime 0x0000000000000001 Informational(4) --providers",
                    "Microsoft-DotNETCore-SampleProfiler 0x0000F00000000000 Informational(4) --profile")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("providersClrEventsIgnored"), providers: "Microsoft-Windows-DotNETRuntime:0x1:4", clrevents: "gc"),
                ExpectProvidersWithMessages(
                    new[]
                    {
                        "The argument --clrevents gc will be ignored because the CLR provider was configured via either --profile or --providers command."
                    },
                    "Microsoft-Windows-DotNETRuntime 0x0000000000000001 Informational(4) --providers")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("clrEventsOnly"), clrevents: "gc+jit"),
                ExpectProviders(
                    "Microsoft-Windows-DotNETRuntime 0x0000000000000011 Informational(4) --clrevents")
            };

            yield return new object[]
            {
                new CollectArgs(output: CreateTraceFile("clrEventsVerbose"), clrevents: "gc+jit", clreventlevel: "5"),
                ExpectProviders(
                    "Microsoft-Windows-DotNETRuntime 0x0000000000000011 Verbose(5) --clrevents")
            };
        }

        private static FileInfo CreateTraceFile(string prefix)
        {
            string fullPath = Path.Combine(TestTraceDir, $"{prefix}_{Guid.NewGuid():N}.nettrace");
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            return new FileInfo(fullPath);
        }

        private static readonly string TestTraceDir = CreateTestTraceDir();

        private static string CreateTestTraceDir()
        {
            string baseDir = AppContext.BaseDirectory;
            string traceDir = Path.Combine(baseDir, "traces");
            Directory.CreateDirectory(traceDir);
            return traceDir;
        }

        private static readonly string[] CommonTail = [
            "Process : <PROCESS>",
            "Output File : <FILE>",
            "Recording trace in progress. Press <Enter> or <Ctrl+C> to exit...",
            "\nTrace completed."
        ];

        private static string[] Expect(params string[] lines)
            => lines.Length == 0 ? CommonTail : [.. lines, .. CommonTail];

        private const string ProviderHeader = "Provider Name Keywords Level Enabled By";

        private static string[] ExpectProviders(params string[] providerLines)
            => Expect([ProviderHeader, .. providerLines]);

        // Supports any number of pre-provider messages before the header.
        private static string[] ExpectProvidersWithMessages(string[] messages, params string[] providerLines)
            => Expect([.. messages, ProviderHeader, .. providerLines]);
    }
}
