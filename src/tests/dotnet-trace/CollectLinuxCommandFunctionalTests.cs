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
            string[] profiles = null,
            FileInfo output = null,
            TimeSpan duration = default,
            string name = "",
            int processId = -1)
        {
            return new CollectLinuxCommandHandler.CollectLinuxArgs(ct,
                                                                   providers ?? Array.Empty<string>(),
                                                                   clrEventLevel,
                                                                   clrEvents,
                                                                   perfEvents ?? Array.Empty<string>(),
                                                                   profiles ?? Array.Empty<string>(),
                                                                   output ?? new FileInfo(CommonOptions.DefaultTraceName),
                                                                   duration,
                                                                   name,
                                                                   processId);
        }

        [Theory]
        [MemberData(nameof(BasicCases))]
        public void CollectLinuxCommandProviderConfigurationConsolidation(object testArgs, string[] expectedLines)
        {
            MockConsole console = new(200, 30);
            var handler = new CollectLinuxCommandHandler(console);
            handler.RecordTraceInvoker = (cmd, len, cb) => 0;
            int exit = handler.CollectLinux((CollectLinuxCommandHandler.CollectLinuxArgs)testArgs);
            if (OperatingSystem.IsLinux())
            {
                Assert.Equal(0, exit);
                console.AssertSanitizedLinesEqual(null, expectedLines);
            }
            else
            {
                Assert.Equal(3, exit);
                console.AssertSanitizedLinesEqual(null, new string[] {
                    "The collect-linux command is only supported on Linux.",
                });
            }
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
                    "", ProviderHeader,
                    FormatProvider("Foo","0000000000000001","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(providers: new[]{"Foo:0x1:4","Bar:0x2:4"}),
                new string[] {
                    "", ProviderHeader,
                    FormatProvider("Foo","0000000000000001","Informational",4,"--providers"),
                    FormatProvider("Bar","0000000000000002","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(profiles: new[]{"cpu-sampling"}),
                new string[] {
                    "No .NET providers were configured.",
                    "",
                    LinuxHeader,
                    LinuxProfile("cpu-sampling"),
                    ""
                }
            };
            yield return new object[] {
                TestArgs(providers: new[]{"Foo:0x1:4"}, profiles: new[]{"cpu-sampling"}),
                new string[] {
                    "", ProviderHeader,
                    FormatProvider("Foo","0000000000000001","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    LinuxProfile("cpu-sampling"),
                    ""
                }
            };
            yield return new object[] {
                TestArgs(clrEvents: "gc", profiles: new[]{"cpu-sampling"}),
                new string[] {
                    "", ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000001","Informational",4,"--clrevents"),
                    "",
                    LinuxHeader,
                    LinuxProfile("cpu-sampling"),
                    ""
                }
            };
            yield return new object[] {
                TestArgs(providers: new[]{"Microsoft-Windows-DotNETRuntime:0x1:4"}, profiles: new[]{"cpu-sampling"}),
                new string[] {
                    "", ProviderHeader,
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
                    "", ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000001","Informational",4,"--providers"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(clrEvents: "gc+jit"),
                new string[] {
                    "", ProviderHeader,
                    FormatProvider("Microsoft-Windows-DotNETRuntime","0000000000000011","Informational",4,"--clrevents"),
                    "",
                    LinuxHeader,
                    ""
                }
            };
            yield return new object[] {
                TestArgs(clrEvents: "gc+jit", clrEventLevel: "5"),
                new string[] {
                    "", ProviderHeader,
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
    }
}
