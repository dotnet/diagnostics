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
            private MemoryStream _eventStream = new();
            public MemoryStream EventStream => _eventStream;
        }

        [Theory]
        [MemberData(nameof(BasicCases))]
        public async Task CollectCommandProviderConfigurationConsolidation(CollectArgs args, string[] expectedSubset)
        {
            MockConsole console = new(200, 30);
            string[] rawLines = await RunAsync(args, console).ConfigureAwait(true);
            console.AssertSanitizedLinesEqual(CollectSanitizer, expectedSubset);

            byte[] expected = Encoding.UTF8.GetBytes(ExpectedPayload);
            Assert.Equal(expected, args.EventStream.ToArray());
        }

        private static async Task<string[]> RunAsync(CollectArgs config, MockConsole console)
        {
            var handler = new CollectCommandHandler();
            handler.StartTraceSessionAsync = (client, cfg, ct) => Task.FromResult<CollectCommandHandler.ICollectSession>(new TestCollectSession());
            handler.ResumeRuntimeAsync = (client, ct) => Task.CompletedTask;
            handler.CursorOperationsSupported = false;
            handler.CollectSessionEventStream = (name) => config.EventStream;
            handler.Console = console;
            handler.IsOutputRedirected = false;

            int exit = await handler.Collect(
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
            return console.Lines;
        }

        private static string[] CollectSanitizer(string[] lines)
        {
            List<string> result = new();
            foreach (string line in lines)
            {
                if (line.StartsWith("Process        :", StringComparison.Ordinal))
                {
                    result.Add("Process        : <PROCESS>");
                }
                else
                {
                    result.Add(line);
                }
            }
            return result.ToArray();
        }

        private sealed class TestCollectSession : CollectCommandHandler.ICollectSession
        {
            private static readonly byte[] _testBytes = Encoding.UTF8.GetBytes(ExpectedPayload);
            private readonly MemoryStream _stream = new(_testBytes);
            public Stream EventStream => _stream;
            public void Stop() {}
            public void Dispose() => _stream.Dispose();
        }

        public static IEnumerable<object[]> BasicCases()
        {
            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("noProviders")),
                ExpectProvidersWithMessages(
                    "noProviders",
                    new[]
                    {
                        "No profile or providers specified, defaulting to trace profile 'cpu-sampling'"
                    },
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"),
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "00000014C14FCCBD", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("singleProvider"), providers: "Foo:0x1:4"),
                ExpectProviders(
                    "singleProvider",
                    FormatProvider("Foo", "0000000000000001", "Informational", 4, "--providers"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("multipleProviders"), providers: "Foo:0x1:4,Bar:0x2:4"),
                ExpectProviders(
                    "multipleProviders",
                    FormatProvider("Foo", "0000000000000001", "Informational", 4, "--providers"),
                    FormatProvider("Bar", "0000000000000002", "Informational", 4, "--providers"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("profileOnly"), profile: "cpu-sampling"),
                ExpectProviders(
                    "profileOnly",
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"),
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "00000014C14FCCBD", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("profileAndProviders"), profile: "cpu-sampling", providers: "Foo:0x1:4"),
                ExpectProviders(
                    "profileAndProviders",
                    FormatProvider("Foo", "0000000000000001", "Informational", 4, "--providers"),
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"),
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "00000014C14FCCBD", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("profileAndClrEventsIgnored"), profile: "cpu-sampling", clrevents: "gc"),
                ExpectProvidersWithMessages(
                    "profileAndClrEventsIgnored",
                    new[]
                    {
                        "The argument --clrevents gc will be ignored because the CLR provider was configured via either --profile or --providers command."
                    },
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"),
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "00000014C14FCCBD", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("profileOverriddenRuntime"), profile: "cpu-sampling", providers: "Microsoft-Windows-DotNETRuntime:0x1:4"),
                ExpectProviders(
                    "profileOverriddenRuntime",
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "0000000000000001", "Informational", 4, "--providers"),
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("providersClrEventsIgnored"), providers: "Microsoft-Windows-DotNETRuntime:0x1:4", clrevents: "gc"),
                ExpectProvidersWithMessages(
                    "providersClrEventsIgnored",
                    new[]
                    {
                        "The argument --clrevents gc will be ignored because the CLR provider was configured via either --profile or --providers command."
                    },
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "0000000000000001", "Informational", 4, "--providers"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("clrEventsOnly"), clrevents: "gc+jit"),
                ExpectProviders(
                    "clrEventsOnly",
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "0000000000000011", "Informational", 4, "--clrevents"))
            };

            yield return new object[]
            {
                new CollectArgs(output: new FileInfo("clrEventsVerbose"), clrevents: "gc+jit", clreventlevel: "5"),
                ExpectProviders(
                    "clrEventsVerbose",
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "0000000000000011", "Verbose", 5, "--clrevents"))
            };
        }

        private const string ProcessPlaceHolder = "Process        : <PROCESS>";
        private static string outputPrefix = $"Output File    : {Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar}";
        private const string ProviderHeader = "Provider Name                           Keywords            Level               Enabled By";
        private static readonly string[] CommonTail = [
            "",
            "Recording trace in progress. Press <Enter> or <Ctrl+C> to exit...",
            "\nTrace completed."
        ];

        private static string[] Expect(string outputFile, params string[] lines)
            => [.. lines, ProcessPlaceHolder, outputPrefix + outputFile, .. CommonTail];

        private static string[] ExpectProviders(string outputFile, params string[] providerLines)
            => Expect(outputFile, ["", ProviderHeader, .. providerLines, ""]);

        private static string[] ExpectProvidersWithMessages(string outputFile, string[] messages, params string[] providerLines)
            => Expect(outputFile, [.. messages, "", ProviderHeader, .. providerLines, ""]);

        private static string FormatProvider(string name, string keywordsHex, string levelName, int levelValue, string enabledBy)
        {
            string display = string.Format("{0, -40}", name) +
                             string.Format("0x{0, -18}", keywordsHex) +
                             string.Format("{0, -8}", $"{levelName}({levelValue})");
            return string.Format("{0, -80}", display) + enabledBy;
        }
    }
}
