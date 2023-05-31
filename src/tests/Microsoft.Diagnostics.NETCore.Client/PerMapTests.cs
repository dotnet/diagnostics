// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.CommonTestRunner;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class PerMapTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public PerMapTests(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
        }

        private static bool DoFilesExist(PerfMapType type, int pid)
        {
            if (type == PerfMapType.All || type == PerfMapType.PerfMap)
            {
                string expectedPerfMapFile = GetPerfMapFileName(pid);
                string expectedPerfInfoFile = GetPerfInfoFileName(pid);

                if (!File.Exists(expectedPerfMapFile) || !File.Exists(expectedPerfInfoFile))
                {
                    return false;
                }
            }

            if (type == PerfMapType.All || type == PerfMapType.JitDump)
            {
                string expectedJitDumpFile = GetJitDumpFileName(pid);
                if (!File.Exists(expectedJitDumpFile))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetTmpDir()
        {
            string tmpDir = Environment.GetEnvironmentVariable("tmpdir");
            if (string.IsNullOrEmpty(tmpDir))
            {
                tmpDir = "/tmp";
            }

            return tmpDir;
        }

        private static string GetJitDumpFileName(int pid) => Path.Combine(GetTmpDir(), $"jit-{pid}.map");

        private static string GetPerfInfoFileName(int pid) => Path.Combine(GetTmpDir(), $"perfinfo-{pid}.map");

        private static string GetPerfMapFileName(int pid) => Path.Combine(GetTmpDir(), $"perf-{pid}.map");

        private string GetMethodNameFromPerfMapLine(string line)
        {
            string[] parts = line.Split(' ');
            StringBuilder builder = new StringBuilder();
            for (int i = 2; i < parts.Length; i++)
            {
                builder.Append(parts[i]);
                builder.Append(' ');
            }

            return builder.ToString();
        }

        private string GetMethodNameFromJitDumpLine(string line) => throw new NotImplementedException();

        private void CheckWellKnownMethods(PerfMapType type, int pid)
        {
            string[] wellKnownNames = new string[] { "Main" };

            if (type == PerfMapType.All || type == PerfMapType.PerfMap)
            {
                bool[] sawNames = new bool[wellKnownNames.Length];
                Array.Fill(sawNames, false);

                string expectedPerfMapFile = GetPerfMapFileName(pid);
                using (StreamReader reader = new StreamReader(expectedPerfMapFile))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string methodName = GetMethodNameFromPerfMapLine(line);
                        for (int i = 0; i < wellKnownNames.Length; ++i)
                        {
                            string candidate = wellKnownNames[i];
                            if (candidate.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                            {
                                sawNames[i] = true;
                            }
                        }
                    }
                }

                for (int i = 0; i < sawNames.Length; ++i)
                {
                    Assert.True(sawNames[i], $"Saw method {wellKnownNames[i]} in PerfMap file");
                }
            }

            if (type == PerfMapType.All || type == PerfMapType.JitDump)
            {
                bool[] sawNames = new bool[wellKnownNames.Length];
                Array.Fill(sawNames, false);

                string expectedJitDumpFile = GetJitDumpFileName(pid);
                using (StreamReader reader = new StreamReader(expectedJitDumpFile))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string methodName = GetMethodNameFromJitDumpLine(line);
                        for (int i = 0; i < wellKnownNames.Length; ++i)
                        {
                            string candidate = wellKnownNames[i];
                            if (candidate.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                            {
                                sawNames[i] = true;
                            }
                        }
                    }
                }

                for (int i = 0; i < sawNames.Length; ++i)
                {
                    Assert.True(sawNames[i], $"Saw method {wellKnownNames[i]} in JitDUmp file");
                }
            }
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task GenerateAllTest(TestConfiguration config)
        {
            await GenerateTestCore(PerfMapType.All, config);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task GeneratePerfMapTest(TestConfiguration config)
        {
            await GenerateTestCore(PerfMapType.PerfMap, config);
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task GenerateJitDumpTest(TestConfiguration config)
        {
            await GenerateTestCore(PerfMapType.JitDump, config);
        }

        private async Task GenerateTestCore(PerfMapType type, TestConfiguration config)
        {
            if (config.RuntimeFrameworkVersionMajor < 8)
            {
                throw new SkipTestException("Not supported on < .NET 8.0");
            }

            if (OS.Kind != OSKind.Linux)
            {
                throw new SkipTestException("Test only supported on Linux");
            }

            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            await runner.Start(testProcessTimeout: 60_000);

            try
            {
                DiagnosticsClientApiShim clientShim = new(new DiagnosticsClient(runner.Pid), true);

                Assert.False(DoFilesExist(type, runner.Pid));
                await clientShim.EnablePerfMap(type);
                await clientShim.DisablePerfMap();
                Assert.True(DoFilesExist(type, runner.Pid));

                CheckWellKnownMethods(type, runner.Pid);

                runner.Stop();
            }
            finally
            {
                runner.PrintStatus();
            }
        }
    }
}
