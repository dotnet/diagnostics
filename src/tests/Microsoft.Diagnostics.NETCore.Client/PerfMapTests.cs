// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
            string tmpDir = Environment.GetEnvironmentVariable("TMPDIR");
            if (string.IsNullOrEmpty(tmpDir))
            {
                tmpDir = "/tmp";
            }

            return tmpDir;
        }

        private static string GetJitDumpFileName(int pid) => Path.Combine(GetTmpDir(), $"jit-{pid}.dump");

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
            string[] wellKnownNames = new string[] { "Tracee.Progam::Main", "System.PackedSpanHelpers::IndexOf" };

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
                            if (methodName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
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
                using (JitDumpParser parser = new JitDumpParser(expectedJitDumpFile, pid))
                {
                    string methodName;
                    while ((methodName = parser.NextMethodName()) != null)
                    {
                        for (int i = 0; i < wellKnownNames.Length; ++i)
                        {
                            string candidate = wellKnownNames[i];
                            if (methodName.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                            {
                                sawNames[i] = true;
                                break;
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

    internal class JitDumpParser : IDisposable
    {
        private class FileHeader
        {
            public uint Magic { get; private set; }
            public uint Version { get; private set; }
            public uint Size { get; private set; }
            public uint Pid { get; private set; }

            public FileHeader(BinaryReader reader)
            {
                // Validate header
                Magic = reader.ReadUInt32();
                Version = reader.ReadUInt32();
                Size = reader.ReadUInt32();
                // Skip padding
                reader.ReadUInt32();
                Pid = reader.ReadUInt32();
                // Ignore timestamp
                reader.ReadUInt64();
                // Ignore flags
                reader.ReadUInt64();
            }
        }

        private class Record
        {
            public uint Id { get; private set; }
            public uint Pid { get; private set; }
            public ulong CodeAddr { get; private set; }
            public ulong CodeSize { get; private set; }
            public string Name { get; private set; }

            public Record(BinaryReader reader)
            {
                Id = reader.ReadUInt32();
                uint totalSize = reader.ReadUInt32();
                // skip timestamp
                reader.ReadUInt64();
                Pid = reader.ReadUInt32();
                // skip tid
                reader.ReadUInt32();
                // skip vma
                reader.ReadUInt64();
                CodeAddr = reader.ReadUInt64();
                CodeSize = reader.ReadUInt64();
                // skip code index
                reader.ReadUInt64();
                Name = ReadNullTerminatedASCIIString(reader);
                // Skip remaining bytes
                int readSoFar = 56 + Name.Length + 1;
                int remainingSize = (int)totalSize - readSoFar;
                reader.ReadBytes(remainingSize);
            }

            private string ReadNullTerminatedASCIIString(BinaryReader reader)
            {
                StringBuilder stringBuilder = new StringBuilder();
                char ch;
                while ((ch = (char)reader.ReadByte()) != 0)
                {
                    stringBuilder.Append(ch);
                }

                return stringBuilder.ToString();
            }
        }

        private readonly BinaryReader _reader;
        private readonly uint _pid;

        public JitDumpParser(string jitDumpFile, int pid)
        {
            _reader = new BinaryReader(new FileStream(jitDumpFile, FileMode.Open));
            _pid = (uint)pid;

            FileHeader header = new FileHeader(_reader);
            Assert.Equal(0x4A695444u, header.Magic);
            Assert.Equal(1u, header.Version);
            Assert.Equal(40u, header.Size);
            Assert.Equal(_pid, header.Pid);

        }

        public void Dispose() => _reader.Dispose();

        internal string NextMethodName()
        {
            if (_reader.PeekChar() == -1)
            {
                return null;
            }

            Record nextRecord = new Record(_reader);
            Assert.Equal(_pid, nextRecord.Pid);
            Assert.NotEqual(0u, nextRecord.CodeAddr);
            Assert.NotEqual(0u, nextRecord.CodeSize);
            return nextRecord.Name;
        }
    }
}
