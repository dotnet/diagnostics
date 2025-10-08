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
                                                                   output ?? new FileInfo(CommonOptions.DefaultTraceName),
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
                console.AssertSanitizedLinesEqual(null, expectedLines: expectedLines);
            }
            else
            {
                Assert.Equal((int)ReturnCode.PlatformNotSupportedError, exitCode);
                console.AssertSanitizedLinesEqual(null, expectedLines: new string[] {
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
                console.AssertSanitizedLinesEqual(null, true, expectedLines: expectedException);
            }
            else
            {
                Assert.Equal((int)ReturnCode.PlatformNotSupportedError, exitCode);
                console.AssertSanitizedLinesEqual(null, expectedLines: new string[] {
                    "The collect-linux command is only supported on Linux.",
                });
            }
        }

        private static int Run(object args, MockConsole console)
        {
            var handler = new CollectLinuxCommandHandler(console);
            handler.RecordTraceInvoker = (cmd, len, cb) => 0;
            return handler.CollectLinux((CollectLinuxCommandHandler.CollectLinuxArgs)args);
        }


        public static IEnumerable<object[]> BasicCases()
        {
            yield return new object[] {
                TestArgs(),
                new string[] {
                    "No providers, profiles, ClrEvents, or PerfEvents were specified, defaulting to trace profiles 'dotnet-common' + 'cpu-sampling'.",
                    "",
                    ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","000000100003801D","Informational",4,"--profile"),
                    "",
                    LinuxHeader,
                    LinuxProfile("cpu-sampling"),
                    ""
                }
            };
            yield return new object[] {
                TestArgs(providers: new[]{"Foo:0x1:4"}),
                new string[] {
                    "",
                    ProviderHeader,
                    FormatProvider("Foo","0000000000000001","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(providers: new[]{"Foo:0x1:4","Bar:0x2:4"}),
                new string[] {
                    "",
                    ProviderHeader,
                    FormatProvider("Foo","0000000000000001","Informational",4,"--providers"),
                    FormatProvider("Bar","0000000000000002","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(profile: new[]{"cpu-sampling"}),
                new string[] {
                    "No .NET providers were configured.",
                    "",
                    LinuxHeader,
                    LinuxProfile("cpu-sampling"),
                    ""
                }
            };
            yield return new object[] {
                TestArgs(providers: new[]{"Foo:0x1:4"}, profile: new[]{"cpu-sampling"}),
                new string[] {
                    "",
                    ProviderHeader,
                    FormatProvider("Foo","0000000000000001","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    LinuxProfile("cpu-sampling"),
                    ""
                }
            };
            yield return new object[] {
                TestArgs(clrEvents: "gc", profile: new[]{"cpu-sampling"}),
                new string[] {
                    "",
                    ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000001","Informational",4,"--clrevents"),
                    "",
                    LinuxHeader,
                    LinuxProfile("cpu-sampling"),
                    ""
                }
            };
            yield return new object[] {
                TestArgs(providers: new[]{"Microsoft-Windows-DotNETRuntime:0x1:4"}, profile: new[]{"cpu-sampling"}),
                new string[] {
                    "",
                    ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000001","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    LinuxProfile("cpu-sampling"),
                    ""
                }
            };
            yield return new object[] {
                TestArgs(providers: new[]{"Microsoft-Windows-DotNETRuntime:0x1:4"}, clrEvents: "gc"),
                new string[] {
                    "Warning: The CLR provider was already specified through --providers or --profile. Ignoring --clrevents.",
                    "",
                    ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000001","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(clrEvents: "gc+jit"),
                new string[] {
                    "",
                    ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000011","Informational",4,"--clrevents"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(clrEvents: "gc+jit", clrEventLevel: "5"),
                new string[] {
                    "",
                    ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000011","Verbose",5,"--clrevents"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(perfEvents: new[]{"sched:sched_switch"}),
                new string[] {
                    "No .NET providers were configured.",
                    "",
                    LinuxHeader,
                    LinuxPerfEvent("sched:sched_switch"),
                    ""
                }
            };
        }

        public static IEnumerable<object[]> InvalidProviders()
        {
            yield return new object[]
            {
                TestArgs(profile: new[] { "dotnet-sampled-thread-time" }),
                new [] { FormatException("The specified profile 'dotnet-sampled-thread-time' does not apply to `dotnet-trace collect-linux`.", "System.ArgumentException") }
            };

            yield return new object[]
            {
                TestArgs(profile: new[] { "unknown" }),
                new [] { FormatException("Invalid profile name: unknown", "System.ArgumentException") }
            };

            yield return new object[]
            {
                TestArgs(providers: new[] { "Foo:::Bar=0", "Foo:::Bar=1" }),
                new [] { FormatException($"Provider \"Foo\" is declared multiple times with filter arguments.", "System.ArgumentException") }
            };

            yield return new object[]
            {
                TestArgs(clrEvents: "unknown"),
                new [] { FormatException("unknown is not a valid CLR event keyword", "System.ArgumentException") }
            };

            yield return new object[]
            {
                TestArgs(clrEvents: "gc", clrEventLevel: "unknown"),
                new [] { FormatException("Unknown EventLevel: unknown", "System.ArgumentException") }
            };
        }

        private const string ProviderHeader = "Provider Name                           Keywords            Level               Enabled By";
        private static string LinuxHeader => $"{"Linux Events",-80}Enabled By";
        private static string LinuxProfile(string name) => $"{name,-80}--profile";
        private static string LinuxPerfEvent(string spec) => $"{spec,-80}--perf-events";
        private static string FormatProvider(string name, string keywordsHex, string levelName, int levelValue, string enabledBy)
        {
            string display = string.Format("{0, -40}", name) +
                             string.Format("0x{0, -18}", keywordsHex) +
                             string.Format("{0, -8}", $"{levelName}({levelValue})");
            return string.Format("{0, -80}", display) + enabledBy;
        }
        private static string FormatException(string message, string exceptionType) => $"[ERROR] {exceptionType}: {message}";
    }
}
