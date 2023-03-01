// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.TestHelpers;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.CommonTestRunner
{
    public class TestRunner : IAsyncDisposable
    {
        private const string _timeFormat = "mm\\:ss\\.fff";
        private readonly IndentedTestOutputHelper _outputHelper;
        private readonly ProcessRunner _runner;
        private readonly DateTime _startTime;
        private readonly NamedPipeServerStream _pipeServer;

        static TestRunner()
        {
            TestConfiguration.BaseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        public static IEnumerable<object[]> Configurations => GetConfigurations("TestName", null);

        public static IEnumerable<object[]> GetConfigurations(string key, string value)
        {
            return TestRunConfiguration.Instance.Configurations.Where((c) => key == null || c.AllSettings.GetValueOrDefault(key) == value).Select(c => new[] { c });
        }

        public static async Task<TestRunner> Create(TestConfiguration config, ITestOutputHelper outputHelper, string testExeName, string testArguments = null, bool usePipe = true)
        {
            Debug.Assert(config != null);
            Debug.Assert(outputHelper != null);
            Debug.Assert(testExeName != null);

            // Restore and build the debuggee.
            DebuggeeConfiguration debuggeeConfig = await DebuggeeCompiler.Execute(config, testExeName, outputHelper);

            // Get the full debuggee launch command line (includes the host if required)
            string exePath = debuggeeConfig.BinaryExePath;
            string pipeName = null;

            var arguments = new StringBuilder();
            var managedArguments = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(config.HostExe))
            {
                exePath = config.HostExe;
                if (!string.IsNullOrWhiteSpace(config.HostArgs))
                {
                    arguments.Append(config.HostArgs);
                }
                managedArguments.Append(debuggeeConfig.BinaryExePath);
            }
            if (usePipe)
            {
                pipeName = Guid.NewGuid().ToString();
                managedArguments.AppendSpace();
                managedArguments.Append(pipeName);
            }
            if (testArguments != null)
            {
                managedArguments.AppendSpace();
                managedArguments.Append(testArguments);
            }
            arguments.AppendSpace();
            arguments.Append(managedArguments);

            // Create the native debugger process running
            ProcessRunner processRunner = new ProcessRunner(exePath, arguments.ToString()).
                WithEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0").
                WithEnvironmentVariable("DOTNET_ROOT", config.DotNetRoot).
                WithLog(outputHelper);

            return new TestRunner(config, outputHelper, processRunner, pipeName, managedArguments.ToString());
        }

        private TestRunner(TestConfiguration config, ITestOutputHelper outputHelper, ProcessRunner runner, string pipeName, string managedArguments)
        {
            Configuration = config;
            _outputHelper = new IndentedTestOutputHelper(outputHelper);
            _runner = runner;
            _startTime = DateTime.Now;
            _pipeServer = pipeName is not null ? new NamedPipeServerStream(pipeName) : null;
            ManagedArguments = managedArguments;
            outputHelper.WriteLine($"[{_startTime}] Test runner created");
        }

        /// <summary>
        /// Test configuration
        /// </summary>
        public TestConfiguration Configuration { get; }

        /// <summary>
        /// Tracee process id
        /// </summary>
        public int Pid => _runner.ProcessId;

        /// <summary>
        /// The host exe path
        /// </summary>
        public string ExePath => _runner.ExePath;

        /// <summary>
        /// All the arguments including the managed program dll
        /// </summary>
        public string Arguments => _runner.Arguments;

        /// <summary>
        /// The managed app path and the app arguments (doesn't include the host exe and host args).
        /// </summary>
        public string ManagedArguments { get; }

        /// <summary>
        /// Add environment variable. Needs to be called before Start().
        /// </summary>
        /// <param name="key">variable name</param>
        /// <param name="value">value</param>
        public void AddEnvVar(string key, string value) => _runner.WithEnvironmentVariable(key, value);

        /// <summary>
        /// Start the tracee.
        /// </summary>
        /// <param name="testProcessTimeout">Cancel process/fail test after this time. Default 30 secs</param>
        /// <param name="waitForTracee">Wait for tracee if true. Default true. Set to false when the process is suspended.</param>
        public async Task Start(int testProcessTimeout = 30_000, bool waitForTracee = true)
        {
            // Set the target process time out
            _runner.WithTimeout(TimeSpan.FromMilliseconds(testProcessTimeout));

            try
            {
                _runner.Start();
            }
            catch (Exception ex)
            {
                _outputHelper.WriteLine($"[{_startTime}] Could not start process: {_runner.ExePath} {ex}");
                throw;
            }
            WriteLine("Successfully started process");

            if (waitForTracee)
            {
                await WaitForTracee();
            }
            else
            {
                // Retry getting the module count because we can catch the process during startup and it fails temporarily.
                for (int retry = 0; retry < 5; retry++)
                {
                    try
                    {
                        WriteLine($"Have total {_runner.ModuleCount} modules loaded");
                        break;
                    }
                    catch (Win32Exception)
                    {
                    }
                }

                // Block until we see the IPC channel created, or until timeout specified.
                try
                {
                    var source = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await Task.Run(cancellationToken: source.Token, action: () => {
                        while (true)
                        {
                            string[] matchingFiles;
                            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            {
                                // On Windows, we wait until the named pipe is created.
                                matchingFiles = Directory.GetFiles(@"\\.\pipe\", $"dotnet-diagnostic-{_runner.ProcessId}*");
                            }
                            else
                            {
                                // On Linux, we wait until the socket is created.
                                matchingFiles = Directory.GetFiles(Path.GetTempPath(), $"dotnet-diagnostic-{_runner.ProcessId}-*-socket"); // Try best match.
                            }
                            if (matchingFiles.Length > 0)
                            {
                                break;
                            }
                            Task.Delay(100);
                        }
                    });
                }
                catch (TaskCanceledException)
                {
                }
            }
        }

        public void Stop()
        {
            WriteLine("Stopping");
            try
            {
                // Make a good will attempt to end the tracee process and its process tree
                _runner.Kill(KillReason.Stopped);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Stopping {Pid} failed {ex}");
            }
        }

        public void PrintStatus()
        {
            if (_runner.WaitForExit().IsCompleted)
            {
                WriteLine($"Process status: Exited 0x{_runner.ExitCode:X}");
            }
            else
            {
                WriteLine($"Process status: Running");
            }
        }

        public async Task WaitForExit(TimeSpan timeout)
        {
            WriteLine("WaitForExitAsync");
            Task timeoutTask = Task.Delay(timeout);
            Task result = await Task.WhenAny(_runner.WaitForExit(), timeoutTask);
            if (result == timeoutTask)
            {
                throw new TaskCanceledException($"WaitForExitAsync timed out {Pid}");
            }
            WriteLine("WaitForExitAsync DONE");
        }

        public async Task WaitForTracee()
        {
            if (_pipeServer is not null)
            {
                WriteLine("WaitForTracee");
                try
                {
                    var source = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    await _pipeServer.WaitForConnectionAsync(source.Token);
                    WriteLine("WaitForTracee: DONE");
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException)
                {
                    WriteLine($"WaitForTracee: canceled {ex}");
                }
            }
        }

        public void WakeupTracee()
        {
            if (_pipeServer is not null)
            {
                WriteLine("WakeupTracee");
                try
                {
                    _pipeServer.WriteByte(42);
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
                {
                    Trace.TraceError($"WakeupTracee {Pid} failed {ex}");
                }
                WriteLine("WakeupTracee DONE");
            }
        }

        public void WaitForSignal()
        {
            if (_pipeServer is not null)
            {
                WriteLine("WaitForSignal");
                try
                {
                    int signal = _pipeServer.ReadByte();
                    WriteLine($"WaitForSignal DONE {signal}");
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
                {
                    WriteLine($"WaitForSignal failed {ex}");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            WriteLine("Disposing");
            WakeupTracee();
            try
            {
                await WaitForExit(TimeSpan.FromSeconds(10));
            }
            catch (TaskCanceledException)
            {
                WriteLine("Disposing: Did not exit within timeout period. Forcefully stopping process.");
                Stop();
            }
            _pipeServer?.Dispose();
            _runner.Dispose();
        }

        public void WriteLine(string message)
        {
            TimeSpan offset = _startTime - DateTime.Now;
            _outputHelper.WriteLine($"[{offset.ToString(_timeFormat)}] {Pid} {message}");
        }
    }

    public static class TestConfigExtensions
    {
        public static string DotNetTraceHost(this TestConfiguration config)
        {
            string dotnetTraceHost = config.GetValue("DotNetTraceHost");
            return TestConfiguration.MakeCanonicalPath(dotnetTraceHost);
        }

        public static string DotNetTracePath(this TestConfiguration config)
        {
            string dotnetTracePath = config.GetValue("DotNetTracePath");
            return TestConfiguration.MakeCanonicalPath(dotnetTracePath);
        }

        public static void AppendSpace(this StringBuilder sb)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }
        }
    }
}
