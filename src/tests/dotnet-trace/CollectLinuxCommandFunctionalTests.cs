// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Diagnostics.Tests.Common;
using Microsoft.Diagnostics.Tools.Trace;
using Microsoft.Internal.Common.Utils;
using Xunit;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class CollectLinuxCommandFunctionalTests
    {
        private static CollectLinuxCommandHandler.CollectLinuxArgs TestArgs(
            CancellationToken ct = default,
            string[] providers = null,
            string clrEventLevel = "",
            string clrEvents = "",
            string[] perfEvents = null,
            string[] profile = null,
            FileInfo output = null,
            TimeSpan duration = default)
        {
            return new CollectLinuxCommandHandler.CollectLinuxArgs(ct,
                                                                   providers ?? Array.Empty<string>(),
                                                                   clrEventLevel,
                                                                   clrEvents,
                                                                   perfEvents ?? Array.Empty<string>(),
                                                                   profile ?? Array.Empty<string>(),
                                                                   output ?? new FileInfo("trace.nettrace"),
                                                                   duration);
        }

        [Theory]
        [MemberData(nameof(BasicCases))]
        public void CollectLinuxCommandProviderConfigurationConsolidation(object testArgs, string[] expectedLines)
        {
            MockConsole console = new(200, 30);
            int exitCode = Run(testArgs, console);
            if (OperatingSystem.IsLinux())
            {
                Assert.Equal((int)ReturnCode.Ok, exitCode);
                console.AssertSanitizedLinesEqual(CollectLinuxSanitizer, expectedLines);
            }
            else
            {
                Assert.Equal((int)ReturnCode.PlatformNotSupportedError, exitCode);
                console.AssertSanitizedLinesEqual(null, new string[] {
                    "The collect-linux command is only supported on Linux.",
                });
            }
        }

        [Theory]
        [MemberData(nameof(InvalidProviders))]
        public void CollectLinuxCommandProviderConfigurationConsolidation_Throws(object testArgs, string[] expectedException)
        {
            MockConsole console = new(200, 30);
            int exitCode = Run(testArgs, console);
            if (OperatingSystem.IsLinux())
            {
                Assert.Equal((int)ReturnCode.TracingError, exitCode);
                console.AssertSanitizedLinesEqual(null, expectedException);
            }
            else
            {
                Assert.Equal((int)ReturnCode.PlatformNotSupportedError, exitCode);
                console.AssertSanitizedLinesEqual(null, new string[] {
                    "The collect-linux command is only supported on Linux.",
                });
            }
        }

        private static int Run(object args, MockConsole console)
        {
            var handler = new CollectLinuxCommandHandler(console);
            handler.RecordTraceInvoker = (cmd, len, cb) => {
                cb(3, IntPtr.Zero, UIntPtr.Zero);
                return 0;
            };
            return handler.CollectLinux((CollectLinuxCommandHandler.CollectLinuxArgs)args);
        }

        private static string[] CollectLinuxSanitizer(string[] lines)
        {
            List<string> result = new();
            foreach (string line in lines)
            {
                if (line.Contains("Recording trace.", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("[dd:hh:mm:ss]\tRecording trace.");
                }
                else
                {
                    result.Add(line);
                }
            }
            return result.ToArray();
        }

        public static IEnumerable<object[]> BasicCases()
        {
            yield return new object[] {
                TestArgs(),
                ExpectProvidersAndLinuxWithMessages(
                    new[]{"No providers, profiles, ClrEvents, or PerfEvents were specified, defaulting to trace profiles 'dotnet-common' + 'cpu-sampling'."},
                    new[]{FormatProvider("Microsoft-Windows-DotNETRuntime","000000100003801D","Informational",4,"--profile")},
                    new[]{LinuxProfile("cpu-sampling")})
            };

            yield return new object[] {
                TestArgs(providers: new[]{"Foo:0x1:4"}),
                ExpectProvidersAndLinux(
                    new[]{FormatProvider("Foo","0000000000000001","Informational",4,"--providers")},
                    Array.Empty<string>())
            };

            yield return new object[] {
                TestArgs(providers: new[]{"Foo:0x1:4","Bar:0x2:4"}),
                ExpectProvidersAndLinux(
                    new[]{
                        FormatProvider("Foo","0000000000000001","Informational",4,"--providers"),
                        FormatProvider("Bar","0000000000000002","Informational",4,"--providers")
                    },
                    Array.Empty<string>())
            };

            yield return new object[] {
                TestArgs(profile: new[]{"cpu-sampling"}),
                ExpectProvidersAndLinuxWithMessages(
                    new[]{"No .NET providers were configured."},
                    Array.Empty<string>(),
                    new[]{LinuxProfile("cpu-sampling")})
            };

            yield return new object[] {
                TestArgs(providers: new[]{"Foo:0x1:4"}, profile: new[]{"cpu-sampling"}),
                ExpectProvidersAndLinux(
                    new[]{FormatProvider("Foo","0000000000000001","Informational",4,"--providers")},
                    new[]{LinuxProfile("cpu-sampling")})
            };

            yield return new object[] {
                TestArgs(clrEvents: "gc", profile: new[]{"cpu-sampling"}),
                ExpectProvidersAndLinux(
                    new[]{FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000001","Informational",4,"--clrevents")},
                    new[]{LinuxProfile("cpu-sampling")})
            };

            yield return new object[] {
                TestArgs(providers: new[]{"Microsoft-Windows-DotNETRuntime:0x1:4"}, profile: new[]{"cpu-sampling"}),
                ExpectProvidersAndLinux(
                    new[]{FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000001","Informational",4,"--providers")},
                    new[]{LinuxProfile("cpu-sampling")})
            };

            yield return new object[] {
                TestArgs(providers: new[]{"Microsoft-Windows-DotNETRuntime:0x1:4"}, clrEvents: "gc"),
                ExpectProvidersAndLinuxWithMessages(
                    new[]{"Warning: The CLR provider was already specified through --providers or --profile. Ignoring --clrevents."},
                    new[]{FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000001","Informational",4,"--providers")},
                    Array.Empty<string>())
            };

            yield return new object[] {
                TestArgs(clrEvents: "gc+jit"),
                ExpectProvidersAndLinux(
                    new[]{FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000011","Informational",4,"--clrevents")},
                    Array.Empty<string>())
            };

            yield return new object[] {
                TestArgs(clrEvents: "gc+jit", clrEventLevel: "5"),
                ExpectProvidersAndLinux(
                    new[]{FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000011","Verbose",5,"--clrevents")},
                    Array.Empty<string>())
            };

            yield return new object[] {
                TestArgs(perfEvents: new[]{"sched:sched_switch"}),
                ExpectProvidersAndLinuxWithMessages(
                    new[]{"No .NET providers were configured."},
                    Array.Empty<string>(),
                    new[]{LinuxPerfEvent("sched:sched_switch")})
            };
        }

        public static IEnumerable<object[]> InvalidProviders()
        {
            yield return new object[]
            {
                TestArgs(profile: new[] { "dotnet-sampled-thread-time" }),
                FormatException("The specified profile 'dotnet-sampled-thread-time' does not apply to `dotnet-trace collect-linux`.")
            };

            yield return new object[]
            {
                TestArgs(profile: new[] { "unknown" }),
                FormatException("Invalid profile name: unknown")
            };

            yield return new object[]
            {
                TestArgs(providers: new[] { "Foo:::Bar=0", "Foo:::Bar=1" }),
                FormatException($"Provider \"Foo\" is declared multiple times with filter arguments.")
            };

            yield return new object[]
            {
                TestArgs(clrEvents: "unknown"),
                FormatException("unknown is not a valid CLR event keyword")
            };

            yield return new object[]
            {
                TestArgs(clrEvents: "gc", clrEventLevel: "unknown"),
                FormatException("Unknown EventLevel: unknown")
            };
        }

        private const string ProviderHeader = "Provider Name                           Keywords            Level               Enabled By";
        private static string LinuxHeader => $"{"Linux Perf Events",-80}Enabled By";
        private static string LinuxProfile(string name) => $"{name,-80}--profile";
        private static string LinuxPerfEvent(string spec) => $"{spec,-80}--perf-events";
        private static string FormatProvider(string name, string keywordsHex, string levelName, int levelValue, string enabledBy)
        {
            string display = string.Format("{0, -40}", name) +
                             string.Format("0x{0, -18}", keywordsHex) +
                             string.Format("{0, -8}", $"{levelName}({levelValue})");
            return string.Format("{0, -80}", display) + enabledBy;
        }
        private static string[] FormatException(string message)
        {
            List<string> result = new();
            result.AddRange(PreviewMessages);
            result.Add($"[ERROR] {message}");
            return result.ToArray();
        }
        private static string DefaultOutputFile => $"Output File    : {Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar}trace.nettrace";
        private static readonly string[] CommonTail = [
            DefaultOutputFile,
            "",
            "[dd:hh:mm:ss]\tRecording trace.",
            "Press <Enter> or <Ctrl-C> to exit...",
        ];
        private static string[] PreviewMessages = [
            "==========================================================================================",
            "The collect-linux verb is a new preview feature and relies on an updated version of the",
            ".nettrace file format. The latest PerfView release supports these trace files but other",
            "ways of using the trace file may not work yet. For more details, see the docs at",
            "https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace.",
            "=========================================================================================="
            ];

        private static string[] ExpectProvidersAndLinux(string[] dotnetProviders, string[] linuxPerfEvents)
            => ExpectProvidersAndLinuxWithMessages(Array.Empty<string>(), dotnetProviders, linuxPerfEvents);

        private static string[] ExpectProvidersAndLinuxWithMessages(string[] messages, string[] dotnetProviders, string[] linuxPerfEvents)
        {
            List<string> result = new();

            result.AddRange(PreviewMessages);

            if (messages.Length > 0)
            {
                result.AddRange(messages);
            }
            result.Add("");

            if (dotnetProviders.Length > 0)
            {
                result.Add(ProviderHeader);
                result.AddRange(dotnetProviders);
                result.Add("");
            }

            if (linuxPerfEvents.Length > 0)
            {
                result.Add(LinuxHeader);
                result.AddRange(linuxPerfEvents);
            }
            else
            {
                result.Add("No Linux Perf Events enabled.");
            }
            result.Add("");

            result.AddRange(CommonTail);

            return result.ToArray();
        }
    }
}
