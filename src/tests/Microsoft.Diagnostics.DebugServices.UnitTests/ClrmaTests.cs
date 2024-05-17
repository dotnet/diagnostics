// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class ClrmaTests : IDisposable
    {
        private const string ListenerName = "ClrmaTests";

        private static IEnumerable<object[]> _configurations;

        public static IEnumerable<object[]> GetConfigurations()
        {
            return _configurations ??= TestRunConfiguration.Instance.Configurations
                .Where((config) => config.IsTestDbgEng() && config.AllSettings.ContainsKey("DumpFile"))
                .Select((config) => new TestDbgEng(config))
                .Select((host) => new[] { host })
                .ToImmutableArray();
        }

        private ITestOutputHelper Output { get; set; }

        public ClrmaTests(ITestOutputHelper output)
        {
            Output = output;
        }

        void IDisposable.Dispose()
        {
        }

        [SkippableTheory, MemberData(nameof(GetConfigurations))]
        public void BangClrmaTests(TestHost host)
        {
            ITarget target = host.Target;
            Assert.NotNull(target);

            if (target.Host.HostType != HostType.DbgEng)
            {
                throw new SkipTestException("Test only supported on dbgeng");
            }
            IDiagnosticLoggingService logging = target.Services.GetService<IDiagnosticLoggingService>();
            bool enabled = logging.IsEnabled;
            string filePath = logging.FilePath;
            logging.Disable();
            try
            {
                bool Filter(string line)
                {
                    return !string.IsNullOrWhiteSpace(line) && !line.Contains("Command: ");
                }
                // First try the built-in CLRMA provider and turn off the EFN_StackTrace extension API so
                // the built-in provider fallbacks back to using !clrstack to get thread stack traces.
                host.ExecuteHostCommand("!sos clrmaconfig -disable -logging -dac -stacktrace");
                IEnumerable<string> builtIn = host.ExecuteHostCommand("!clrma").Where(Filter);

                // Now try the direct DAC CLRMA provider in SOS
                host.ExecuteHostCommand("!sos clrmaconfig -enable -dac -stacktrace");
                IEnumerable<string> directDac = host.ExecuteHostCommand("!clrma").Where(Filter);

                IEnumerable<string> diff1 = builtIn.Except(directDac);
                IEnumerable<string> diff2 = directDac.Except(builtIn);

                if (diff1.Any() || diff2.Any())
                {
                    string builtInFile = Path.GetTempFileName();
                    string directDacFile = Path.GetTempFileName();
                    File.WriteAllLines(builtInFile, builtIn);
                    File.WriteAllLines(directDacFile, directDac);

                    Output.WriteLine(string.Empty);
                    Output.WriteLine("------------------------------------");
                    Output.WriteLine($"Built-In Provider: {builtInFile}");
                    Output.WriteLine("------------------------------------");
                    foreach (string line in diff1)
                    {
                        Output.WriteLine(line);
                    }
                    Output.WriteLine(string.Empty);
                    Output.WriteLine("------------------------------------");
                    Output.WriteLine($"Direct Dac: {directDacFile}");
                    Output.WriteLine("------------------------------------");
                    foreach (string line in diff2)
                    {
                        Output.WriteLine(line);
                    }
                    Assert.Fail();
                }
            }
            finally
            {
                if (enabled)
                {
                    logging.Enable(filePath);
                }
            }
        }

        // The !analyze lines containing this text and the number of lines to skip
        private static List<(string, int)> s_filterList = new()
        {
            ( "Key  : Analysis.CPU.mSec", 2 ),
            ( "Key  : Analysis.Elapsed.mSec", 2 ),
            ( "Key  : Analysis.IO.Other.Mb", 2 ),
            ( "Key  : Analysis.IO.Read.Mb", 2 ),
            ( "Key  : Analysis.IO.Write.Mb", 2 ),
            ( "Key  : Analysis.Init.CPU.mSec", 2 ),
            ( "Key  : Analysis.Init.Elapsed.mSec", 2 ),
            ( "Key  : Analysis.Memory.CommitPeak.Mb", 2 ),
            ( "Timeline: !analyze.Start", 4 ),
            ( "ANALYSIS_SESSION_TIME:", 1 ),
            ( "MANAGED_ANALYSIS_PROVIDER:", 1 ),
            ( "MANAGED_THREAD_ID:", 1 ),
            ( "Stack_Frames_Extraction_Time_(ms):", 1 ),
            ( "Stack_Attribute_Extraction_Time_(ms):", 1 ),
            ( "ANALYSIS_SESSION_ELAPSED_TIME:", 1 ),
        };

        [SkippableTheory, MemberData(nameof(GetConfigurations))]
        public void BangAnalyzeTests(TestHost host)
        {
            ITarget target = host.Target;
            Assert.NotNull(target);

            if (target.Host.HostType != HostType.DbgEng)
            {
                throw new SkipTestException("Test only supported on dbgeng");
            }
            IDiagnosticLoggingService logging = target.Services.GetService<IDiagnosticLoggingService>();
            bool enabled = logging.IsEnabled;
            string filePath = logging.FilePath;
            logging.Disable();
            try
            {
                IEnumerable<string> Filter(ImmutableArray<string> lines)
                {
                    for (int i = 0; i < lines.Length;)
                    {
                        foreach ((string key, int skip) skipItem in s_filterList)
                        {
                            if (lines[i].Contains(skipItem.key))
                            {
                                i += skipItem.skip;
                                continue;
                            }
                        }
                        yield return lines[i];
                        i++;
                    }
                }
                // First try the built-in CLRMA provider. The EFN_StackTrace results doesn't seem to affect the !analyze output
                host.ExecuteHostCommand("!sos clrmaconfig -disable -logging -dac");
                IEnumerable<string> builtIn = Filter(host.ExecuteHostCommand("!analyze -v6"));

                // Now try the direct DAC CLRMA provider in SOS
                host.ExecuteHostCommand("!sos clrmaconfig -enable -dac");
                IEnumerable<string> directDac = Filter(host.ExecuteHostCommand("!analyze -v6"));

                IEnumerable<string> diff1 = builtIn.Except(directDac);
                IEnumerable<string> diff2 = directDac.Except(builtIn);

                if (diff1.Any() || diff2.Any())
                {
                    string builtInFile = Path.GetTempFileName();
                    string directDacFile = Path.GetTempFileName();
                    File.WriteAllLines(builtInFile, builtIn);
                    File.WriteAllLines(directDacFile, directDac);

                    Output.WriteLine(string.Empty);
                    Output.WriteLine("------------------------------------");
                    Output.WriteLine($"Built-In Provider: {builtInFile}");
                    Output.WriteLine("------------------------------------");
                    foreach (string line in diff1)
                    {
                        Output.WriteLine(line);
                    }
                    Output.WriteLine(string.Empty);
                    Output.WriteLine("------------------------------------");
                    Output.WriteLine($"Direct Dac: {directDacFile}");
                    Output.WriteLine("------------------------------------");
                    foreach (string line in diff2)
                    {
                        Output.WriteLine(line);
                    }
                    Assert.Fail();
                }
            }
            finally
            {
                if (enabled)
                {
                    logging.Enable(filePath);
                }
            }
        }
    }
}
