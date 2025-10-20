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
using Microsoft.Internal.Common.Utils;
using Xunit;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class CollectCommandFunctionalTests
    {
        private const string ExpectedPayload = "CollectCommandFunctionalTestsTraceData";

        public sealed record CollectArgs(
            CancellationToken ct = default,
            CommandLineConfiguration cliConfig = null,
            int processId = -1,
            uint buffersize = 1,
            string[] providers = null,
            string[] profile = null,
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
            public int ProcessId => processId == -1 ? Environment.ProcessId : processId;
            public FileInfo Output => new FileInfo("trace.nettrace");
            private MemoryStream _eventStream = new();
            public MemoryStream EventStream => _eventStream;
        }

        [Theory]
        [MemberData(nameof(BasicCases))]
        public async Task CollectCommandProviderConfigurationConsolidation(CollectArgs args, string[] expectedSubset)
        {
            MockConsole console = new(200, 30);
            int exitCode = await RunAsync(args, console).ConfigureAwait(true);
            Assert.Equal((int)ReturnCode.Ok, exitCode);
            console.AssertSanitizedLinesEqual(CollectSanitizer, expectedSubset);

            byte[] expected = Encoding.UTF8.GetBytes(ExpectedPayload);
            Assert.Equal(expected, args.EventStream.ToArray());
        }

        [Theory]
        [MemberData(nameof(InvalidProviders))]
        public async Task CollectCommandInvalidProviderConfiguration_Throws(CollectArgs args, string[] expectedException)
        {
            MockConsole console = new(200, 30);
            int exitCode = await RunAsync(args, console).ConfigureAwait(true);
            Assert.Equal((int)ReturnCode.TracingError, exitCode);
            console.AssertSanitizedLinesEqual(CollectSanitizer, expectedException);
        }

        private static async Task<int> RunAsync(CollectArgs config, MockConsole console)
        {
            var handler = new CollectCommandHandler();
            handler.StartTraceSessionAsync = (client, cfg, ct) => Task.FromResult<CollectCommandHandler.ICollectSession>(new TestCollectSession());
            handler.ResumeRuntimeAsync = (client, ct) => Task.CompletedTask;
            handler.CollectSessionEventStream = (name) => config.EventStream;
            handler.Console = console;

            return await handler.Collect(
                config.ct,
                config.cliConfig,
                config.ProcessId,
                config.Output,
                config.buffersize,
                config.providers ?? Array.Empty<string>(),
                config.profile ?? Array.Empty<string>(),
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
            ).ConfigureAwait(false);
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
                new CollectArgs(),
                ExpectProvidersWithMessages(
                    new[]
                    {
                        "No profile or providers specified, defaulting to trace profiles 'dotnet-common' + 'dotnet-sampled-thread-time'."
                    },
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "000000100003801D", "Informational", 4, "--profile"),
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(providers: new[] { "Foo:0x1:4" }),
                ExpectProviders(
                    FormatProvider("Foo", "0000000000000001", "Informational", 4, "--providers"))
            };

            yield return new object[]
            {
                new CollectArgs(providers: new[] { "Foo:0x1:4", "Bar:0x2:4" }),
                ExpectProviders(
                    FormatProvider("Foo", "0000000000000001", "Informational", 4, "--providers"),
                    FormatProvider("Bar", "0000000000000002", "Informational", 4, "--providers"))
            };

            yield return new object[]
            {
                new CollectArgs(profile: new[] { "dotnet-common", "dotnet-sampled-thread-time" }),
                ExpectProviders(
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "000000100003801D", "Informational", 4, "--profile"),
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(profile: new[] { "dotnet-common", "dotnet-sampled-thread-time" }, providers: new[] { "Foo:0x1:4" }),
                ExpectProviders(
                    FormatProvider("Foo", "0000000000000001", "Informational", 4, "--providers"),
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "000000100003801D", "Informational", 4, "--profile"),
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(profile: new[] { "dotnet-common", "dotnet-sampled-thread-time" }, clrevents: "gc"),
                ExpectProvidersWithMessages(
                    new[]
                    {
                        "Warning: The CLR provider was already specified through --providers or --profile. Ignoring --clrevents."
                    },
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "000000100003801D", "Informational", 4, "--profile"),
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(profile: new[] { "dotnet-common", "dotnet-sampled-thread-time" }, providers: new[] { "Microsoft-Windows-DotNETRuntime:0x1:4" }),
                ExpectProviders(
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "0000000000000001", "Informational", 4, "--providers"),
                    FormatProvider("Microsoft-DotNETCore-SampleProfiler", "0000F00000000000", "Informational", 4, "--profile"))
            };

            yield return new object[]
            {
                new CollectArgs(providers: new[] { "Microsoft-Windows-DotNETRuntime:0x1:4" }, clrevents: "gc"),
                ExpectProvidersWithMessages(
                    new[]
                    {
                        "Warning: The CLR provider was already specified through --providers or --profile. Ignoring --clrevents."
                    },
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "0000000000000001", "Informational", 4, "--providers"))
            };

            yield return new object[]
            {
                new CollectArgs(clrevents: "gc+jit"),
                ExpectProviders(
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "0000000000000011", "Informational", 4, "--clrevents"))
            };

            yield return new object[]
            {
                new CollectArgs(clrevents: "gc+jit", clreventlevel: "5"),
                ExpectProviders(
                    FormatProvider("Microsoft-Windows-DotNETRuntime", "0000000000000011", "Verbose", 5, "--clrevents"))
            };
        }

        public static IEnumerable<object[]> InvalidProviders()
        {
            yield return new object[]
            {
                new CollectArgs(profile: new[] { "cpu-sampling" }),
                new [] { FormatException("The specified profile 'cpu-sampling' does not apply to `dotnet-trace collect`.") }
            };

            yield return new object[]
            {
                new CollectArgs(profile: new[] { "unknown" }),
                new [] { FormatException("Invalid profile name: unknown") }
            };

            yield return new object[]
            {
                new CollectArgs(providers: new[] { "Foo:::Bar=0", "Foo:::Bar=1" }),
                new [] { FormatException($"Provider \"Foo\" is declared multiple times with filter arguments.") }
            };

            yield return new object[]
            {
                new CollectArgs(clrevents: "unknown"),
                new [] { FormatException("unknown is not a valid CLR event keyword") }
            };

            yield return new object[]
            {
                new CollectArgs(clrevents: "gc", clreventlevel: "unknown"),
                new [] { FormatException("Unknown EventLevel: unknown") }
            };
        }

        private static string outputFile = $"Output File    : {Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar}trace.nettrace";
        private const string ProviderHeader = "Provider Name                           Keywords            Level               Enabled By";
        private static readonly string[] CommonTail = [
            "Process        : <PROCESS>",
            outputFile,
            "",
            "",
            "Trace completed."
        ];

        private static string[] ExpectProviders(params string[] providerLines)
            => ExpectProvidersWithMessages(new string[0], providerLines);

        private static string[] ExpectProvidersWithMessages(string[] messages, params string[] providerLines)
            => [.. messages, "", ProviderHeader, .. providerLines, "", .. CommonTail];

        private static string FormatProvider(string name, string keywordsHex, string levelName, int levelValue, string enabledBy)
        {
            string display = string.Format("{0, -40}", name) +
                             string.Format("0x{0, -18}", keywordsHex) +
                             string.Format("{0, -8}", $"{levelName}({levelValue})");
            return string.Format("{0, -80}", display) + enabledBy;
        }

        private static string FormatException(string message) => $"[ERROR] {message}";
    }
}
