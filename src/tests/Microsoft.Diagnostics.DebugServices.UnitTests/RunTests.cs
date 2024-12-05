// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.TestHelpers;
using SOS.Extensions;
using Xunit.Abstractions;
using Xunit.Extensions;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    [Command(Name = "runtests", Help = "Runs the debug services xunit tests.")]
    public class RunTestsCommand : CommandBase, ITestOutputHelper
    {
        [Argument(Help = "Test name: debugservices, clrma or analyze.")]
        public string[] TestNames { get; set; } = Array.Empty<string>();

        [Option(Name = "--testdata", Help = "Test data xml file path.")]
        public string TestDataFile { get; set; }

        [Option(Name = "--dumpfile", Help = "Dump file path.")]
        public string DumpFile { get; set; }

        public override void Invoke()
        {
            ITarget target = Services.GetService<ITarget>() ?? throw new DiagnosticsException("Dump or live session target required");
            string os;
            if (target.OperatingSystem == OSPlatform.Linux)
            {
                os = "linux";
            }
            else if (target.OperatingSystem == OSPlatform.OSX)
            {
                os = "osx";
            }
            else if (target.OperatingSystem == OSPlatform.Windows)
            {
                os = "windows";
            }
            else
            {
                os = "unknown";
            }
            Dictionary<string, string> initialConfig = new()
            {
                ["OS"] = os,
                ["TargetArchitecture"] = target.Architecture.ToString().ToLowerInvariant(),
                ["TestDataFile"] = TestDataFile,
                ["DumpFile"] = DumpFile
            };
            IEnumerable<TestHost> configurations = new[] { new TestDebugger(new TestConfiguration(initialConfig), target, Services) };
            bool passed = true;
            foreach (TestHost host in configurations)
            {
                foreach (string testName in TestNames)
                {
                    try
                    {
                        switch (testName.ToLower())
                        {
                            case "debugservices":
                                {
                                    if (host.TestDataFile == null)
                                    {
                                        throw new DiagnosticsException("TestDataFile option (--testdata) required");
                                    }
                                    if (host.DumpFile == null)
                                    {
                                        throw new DiagnosticsException("DumpFile option (--dumpfile) required");
                                    }
                                    using DebugServicesTests debugServicesTests = new(this);
                                    debugServicesTests.TargetTests(host);
                                    debugServicesTests.ModuleTests(host);
                                    debugServicesTests.ThreadTests(host);
                                    debugServicesTests.RuntimeTests(host);
                                    break;
                                }

                            case "clrma":
                                {
                                    using ClrmaTests clrmaTests = new(this);
                                    clrmaTests.BangClrmaTests(host);
                                    break;
                                }

                            case "analyze":
                                {
                                    using ClrmaTests clrmaTests = new(this);
                                    clrmaTests.BangAnalyzeTests(host);
                                    break;
                                }

                            default:
                                throw new DiagnosticsException($"Invalid test name");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is SkipTestException)
                        {
                            WriteLineWarning($"Test {testName} SKIPPED - {ex.Message}");
                        }
                        else
                        {
                            WriteLineError($"Test {testName} FAILED - {ex.Message}");
                            passed = false;
                        }
                        continue;
                    }
                    WriteLine($"Test {testName} PASSED");
                }
            }
            if (passed)
            {
                WriteLine($"All Tests PASSED");
            }
        }

        #region ITestOutputHelper

        void ITestOutputHelper.WriteLine(string message) => WriteLine(message);

        void ITestOutputHelper.WriteLine(string format, params object[] args) => WriteLine(format, args);

        #endregion
    }

    internal class TestDebugger : TestHost
    {
        private readonly ITarget _target;
        private readonly IServiceProvider _services;
        private readonly CommandService _commandService;

        internal TestDebugger(TestConfiguration config, ITarget target, IServiceProvider services)
            : base(config)
        {
            _target = target;
            _services = services;
            // dotnet-dump adds the CommandService implementation class as a service
            _commandService = services.GetService<CommandService>();
        }

        public override IReadOnlyList<string> ExecuteHostCommand(string commandLine)
        {
            if (HostServices.Instance != null)
            {
                return HostServices.Instance.ExecuteHostCommand(commandLine);
            }
            else
            {
                throw new NotSupportedException("ExecuteHostCommand");
            }
        }

        protected override ITarget GetTarget() => _target;
    }
}
