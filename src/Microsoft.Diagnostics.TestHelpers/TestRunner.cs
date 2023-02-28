// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class TestRunner
    {
        /// <summary>
        /// Run debuggee (without any debugger) and compare the console output to the regex specified.
        /// </summary>
        /// <param name="config">test config to use</param>
        /// <param name="output">output helper</param>
        /// <param name="testName">test case name</param>
        /// <param name="debuggeeName">debuggee name (no path)</param>
        /// <param name="outputRegex">regex to match on console (standard and error) output</param>
        /// <returns></returns>
        public static async Task<int> Run(TestConfiguration config, ITestOutputHelper output, string testName, string debuggeeName, string outputRegex)
        {
            OutputHelper outputHelper = null;
            try
            {
                // Setup the logging from the options in the config file
                outputHelper = ConfigureLogging(config, output, testName);

                // Restore and build the debuggee. The debuggee name is lower cased because the 
                // source directory name has been lowercased by the build system.
                DebuggeeConfiguration debuggeeConfig = await DebuggeeCompiler.Execute(config, debuggeeName.ToLowerInvariant(), outputHelper);

                outputHelper.WriteLine("Starting {0}", testName);
                outputHelper.WriteLine("{");

                // Get the full debuggee launch command line (includes the host if required)
                string exePath = debuggeeConfig.BinaryExePath;
                string arguments = debuggeeConfig.BinaryDirPath;
                if (!string.IsNullOrWhiteSpace(config.HostExe))
                {
                    exePath = config.HostExe;
                    arguments = Environment.ExpandEnvironmentVariables(string.Format("{0} {1} {2}", config.HostArgs, debuggeeConfig.BinaryExePath, debuggeeConfig.BinaryDirPath));
                }

                TestLogger testLogger = new TestLogger(outputHelper.IndentedOutput);
                ProcessRunner processRunner = new ProcessRunner(exePath, arguments).
                    WithLog(testLogger).
                    WithTimeout(TimeSpan.FromMinutes(5));

                processRunner.Start();

                // Wait for the debuggee to finish before getting the debuggee output
                int exitCode = await processRunner.WaitForExit();

                string debuggeeStandardOutput = testLogger.GetStandardOutput();
                string debuggeeStandardError = testLogger.GetStandardError();

                // The debuggee output is all the stdout first and then all the stderr output last
                string debuggeeOutput = debuggeeStandardOutput + debuggeeStandardError;
                if (string.IsNullOrEmpty(debuggeeOutput))
                {
                    throw new Exception("No debuggee output");
                }
                // Remove any CR's in the match string because this assembly is built on Windows (with CRs) and
                // ran on Linux/OS X (without CRs).
                outputRegex = outputRegex.Replace("\r", "");

                // Now match the debuggee output and regex match string
                if (!new Regex(outputRegex, RegexOptions.Multiline).IsMatch(debuggeeOutput))
                {
                    throw new Exception(string.Format("\nDebuggee output:\n\n'{0}'\n\nDid not match the expression:\n\n'{1}'", debuggeeOutput, outputRegex));
                }

                return exitCode;
            }
            catch (Exception ex)
            {
                // Log the exception
                outputHelper?.WriteLine(ex.ToString());
                throw;
            }
            finally
            {
                outputHelper?.WriteLine("}");
                outputHelper?.Dispose();
            }
        }

        /// <summary>
        /// Returns a test config for each PDB type supported by the product/platform.
        /// </summary>
        /// <param name="config">starting config</param>
        /// <returns>new configs for each supported PDB type</returns>
        public static IEnumerable<TestConfiguration> EnumeratePdbTypeConfigs(TestConfiguration config)
        {
            string[] pdbTypes = { "portable", "embedded" };

            if (OS.Kind == OSKind.Windows)
            {
                if (config.IsNETCore)
                {
                    pdbTypes = new string[] { "portable", "full", "embedded" };
                }
                else
                {
                    // Don't change the config on the desktop/projectn projects
                    pdbTypes = new string[] { "" };
                }
            }

            foreach (string pdbType in pdbTypes)
            {
                if (string.IsNullOrWhiteSpace(pdbType))
                {
                    yield return config;
                }
                else
                {
                    yield return config.CloneWithNewDebugType(pdbType);
                }
            }
        }

        /// <summary>
        /// Returns an output helper for the specified config.
        /// </summary>
        /// <param name="config">test config</param>
        /// <param name="output">starting output helper</param>
        /// <param name="testName">test case name</param>
        /// <returns>new output helper</returns>
        public static TestRunner.OutputHelper ConfigureLogging(TestConfiguration config, ITestOutputHelper output, string testName)
        {
            FileTestOutputHelper fileLogger = null;
            ConsoleTestOutputHelper consoleLogger = null;
            if (!string.IsNullOrEmpty(config.LogDirPath))
            {
                string logFileName = $"{testName}.{config.LogSuffix}.log";
                string logPath = Path.Combine(config.LogDirPath, logFileName);
                fileLogger = new FileTestOutputHelper(logPath, FileMode.Append);
            }
            if (config.LogToConsole)
            {
                consoleLogger = new ConsoleTestOutputHelper();
            }
            return new TestRunner.OutputHelper(output, fileLogger, consoleLogger);
        }

        public class OutputHelper : ITestOutputHelper, IDisposable
        {
            private readonly ITestOutputHelper _output;
            private readonly FileTestOutputHelper _fileLogger;
            private readonly ConsoleTestOutputHelper _consoleLogger;

            public readonly ITestOutputHelper IndentedOutput;

            public OutputHelper(ITestOutputHelper output, FileTestOutputHelper fileLogger, ConsoleTestOutputHelper consoleLogger)
            {
                _output = output;
                _fileLogger = fileLogger;
                _consoleLogger = consoleLogger;
                IndentedOutput = new IndentedTestOutputHelper(this);
            }

            public void WriteLine(string message)
            {
                _output.WriteLine(message);
                _fileLogger?.WriteLine(message);
                _consoleLogger?.WriteLine(message);
            }

            public void WriteLine(string format, params object[] args)
            {
                _output.WriteLine(format, args);
                _fileLogger?.WriteLine(format, args);
                _consoleLogger?.WriteLine(format, args);
            }

            public void Dispose()
            {
                _fileLogger?.Dispose();
            }
        }

        public class TestLogger : TestOutputProcessLogger
        {
            private readonly StringBuilder _standardOutput;
            private readonly StringBuilder _standardError;

            public TestLogger(ITestOutputHelper output)
                : base(output)
            {
                lock (this)
                {
                    _standardOutput = new StringBuilder();
                    _standardError = new StringBuilder();
                }
            }

            public string GetStandardOutput()
            {
                lock (this)
                {
                    return _standardOutput.ToString();
                }
            }

            public string GetStandardError()
            {
                lock (this)
                {
                    return _standardError.ToString();
                }
            }

            public override void Write(ProcessRunner runner, string data, ProcessStream stream)
            {
                lock (this)
                {
                    base.Write(runner, data, stream);
                    switch (stream)
                    {
                        case ProcessStream.StandardOut:
                            _standardOutput.Append(data);
                            break;

                        case ProcessStream.StandardError:
                            _standardError.Append(data);
                            break;
                    }
                }
            }

            public override void WriteLine(ProcessRunner runner, string data, ProcessStream stream)
            {
                lock (this)
                {
                    base.WriteLine(runner, data, stream);
                    switch (stream)
                    {
                        case ProcessStream.StandardOut:
                            _standardOutput.AppendLine(data);
                            break;

                        case ProcessStream.StandardError:
                            _standardError.AppendLine(data);
                            break;
                    }
                }
            }
        }
    }
}
