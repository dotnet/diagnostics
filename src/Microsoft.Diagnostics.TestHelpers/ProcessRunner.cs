// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// Executes a process and logs the output
    /// </summary>
    /// <remarks>
    /// The intended lifecycle is:
    ///   a) Create a new ProcessRunner
    ///   b) Use the various WithXXX methods to modify the configuration of the process to launch
    ///   c) await RunAsync() to start the process and wait for it to terminate. Configuration
    ///      changes are no longer possible
    ///   d) While waiting for RunAsync(), optionally call Kill() one or more times. This will expedite
    ///      the termination of the process but there is no guarantee the process is terminated by
    ///      the time Kill() returns.
    ///
    ///   Although the entire API of this type has been designed to be thread-safe, its typical that
    ///   only calls to Kill() and property getters invoked within the logging callbacks will be called
    ///   asynchronously.
    /// </remarks>
    public class ProcessRunner : IDisposable
    {
        // All of the locals might accessed from multiple threads and need to read/written under
        // the _lock. We also use the lock to synchronize property access on the process object.
        //
        // Be careful not to cause deadlocks by calling the logging callbacks with the lock held.
        // The logger has its own lock and it will hold that lock when it calls into property getters
        // on this type.
        private readonly object _lock = new();
        private readonly List<IProcessLogger> _loggers;
        private readonly Process _p;
        private DateTime _startTime;
        private TimeSpan _timeout;
        private ITestOutputHelper _traceOutput;
        private int? _expectedExitCode;
        private readonly TaskCompletionSource<Process> _waitForProcessStartTaskSource;
        private readonly Task<int> _waitForExitTask;
        private readonly Task _timeoutProcessTask;
        private readonly Task _readStdOutTask;
        private readonly Task _readStdErrTask;
        private readonly CancellationTokenSource _cancelSource;
        private readonly string _replayCommand;
        private KillReason? _killReason;

        public ProcessRunner(string exePath, string arguments, string replayCommand = null)
        {
            ProcessStartInfo psi = new();
            psi.FileName = exePath;
            psi.Arguments = arguments;
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            lock (_lock)
            {
                _p = new Process();
                _p.StartInfo = psi;
                _p.EnableRaisingEvents = false;
                _loggers = new List<IProcessLogger>();
                _timeout = TimeSpan.FromMinutes(10);
                _cancelSource = new CancellationTokenSource();
                _killReason = null;
                _waitForProcessStartTaskSource = new TaskCompletionSource<Process>();
                Task<Process> startTask = _waitForProcessStartTaskSource.Task;

                // unfortunately we can't use the default Process stream reading because it only returns full lines and we have scenarios
                // that need to receive the output before the newline character is written
                _readStdOutTask = startTask.ContinueWith(t => {
                    ReadStreamToLoggers(_p.StandardOutput, ProcessStream.StandardOut, _cancelSource.Token);
                },
                _cancelSource.Token, TaskContinuationOptions.LongRunning, TaskScheduler.Default);

                _readStdErrTask = startTask.ContinueWith(t => {
                    ReadStreamToLoggers(_p.StandardError, ProcessStream.StandardError, _cancelSource.Token);
                },
                _cancelSource.Token, TaskContinuationOptions.LongRunning, TaskScheduler.Default);

                _timeoutProcessTask = startTask.ContinueWith(async t => {
                    await Task.Delay(_timeout, _cancelSource.Token).ConfigureAwait(false);
                    Kill(KillReason.TimedOut);
                },
                _cancelSource.Token, TaskContinuationOptions.LongRunning, TaskScheduler.Default);

                _waitForExitTask = InternalWaitForExit(startTask, _readStdOutTask, _readStdErrTask);

                if (replayCommand == null)
                {
                    _replayCommand = ExePath + " " + Arguments;
                }
                else
                {
                    _replayCommand = replayCommand;
                }
            }
        }

        public string ReplayCommand
        {
            get { lock (_lock) { return _replayCommand; } }
        }

        public ProcessRunner RemoveEnvironmentVariable(string key)
        {
            lock (_lock)
            {
                _p.StartInfo.Environment.Remove(key);
            }
            return this;
        }

        // Remove COMPlus_ fallback once minimum supported runtime is .NET 8
        public ProcessRunner WithRuntimeConfiguration(string key, string value) =>
            WithEnvironmentVariable($"DOTNET_{key}", value)
            .WithEnvironmentVariable($"COMPlus_{key}", value);

        public ProcessRunner WithEnvironmentVariable(string key, string value)
        {
            lock (_lock)
            {
                _p.StartInfo.Environment[key] = value;
            }
            return this;
        }

        public ProcessRunner WithWorkingDirectory(string workingDirectory)
        {
            lock (_lock)
            {
                _p.StartInfo.WorkingDirectory = workingDirectory;
            }
            return this;
        }

        public ProcessRunner WithLog(IProcessLogger logger)
        {
            lock (_lock)
            {
                _loggers.Add(logger);
            }
            return this;
        }

        public ProcessRunner WithLog(ITestOutputHelper output)
        {
            lock (_lock)
            {
                _loggers.Add(new TestOutputProcessLogger(output));
            }
            return this;
        }

        public ProcessRunner WithDiagnosticTracing(ITestOutputHelper traceOutput)
        {
            lock (_lock)
            {
                _traceOutput = new ConsoleTestOutputHelper(traceOutput);
            }
            return this;
        }

        public IProcessLogger[] Loggers
        {
            get { lock (_lock) { return _loggers.ToArray(); } }
        }

        public ProcessRunner WithTimeout(TimeSpan timeout)
        {
            lock (_lock)
            {
                _timeout = timeout;
            }
            return this;
        }

        public ProcessRunner WithExpectedExitCode(int expectedExitCode)
        {
            lock (_lock)
            {
                _expectedExitCode = expectedExitCode;
            }
            return this;
        }

        public string ExePath
        {
            get { lock (_lock) { return _p.StartInfo.FileName; } }
        }

        public string Arguments
        {
            get { lock (_lock) { return _p.StartInfo.Arguments; } }
        }

        public string WorkingDirectory
        {
            get { lock (_lock) { return _p.StartInfo.WorkingDirectory; } }
        }

        public int ProcessId
        {
            get { lock (_lock) { return _p.Id; } }
        }

        public Dictionary<string, string> EnvironmentVariables
        {
            get { lock (_lock) { return new Dictionary<string, string>(_p.StartInfo.Environment); } }
        }

        public bool IsStarted
        {
            get { lock (_lock) { return _waitForProcessStartTaskSource.Task.IsCompleted; } }
        }

        public DateTime StartTime
        {
            get { lock (_lock) { return _startTime; } }
        }

        public int ExitCode
        {
            get { lock (_lock) { return _p.ExitCode; } }
        }

        public int ModuleCount
        {
            get { lock (_lock) { return _p.Modules.Count; } }
        }

        public void StandardInputWriteLine(string line)
        {
            IProcessLogger[] loggers = null;
            StreamWriter inputStream = null;
            lock (_lock)
            {
                loggers = _loggers.ToArray();
                inputStream = _p.StandardInput;
            }
            foreach (IProcessLogger logger in loggers)
            {
                logger.WriteLine(this, line, ProcessStream.StandardIn);
            }
            inputStream.WriteLine(line);
            inputStream.Flush();
        }

        public Task<int> Run()
        {
            Start();
            return WaitForExit();
        }

        public Task<int> WaitForExit()
        {
            lock (_lock)
            {
                return _waitForExitTask;
            }
        }

        public void Dispose()
        {
            Process p = null;
            lock (_lock)
            {
                p = _p;
            }
            p?.Dispose();
        }

        public ProcessRunner Start()
        {
            Process p = null;
            lock (_lock)
            {
                p = _p;
            }
            // this is safe to call on multiple threads, it only launches the process once
            _ = p.Start();

            IProcessLogger[] loggers = null;
            lock (_lock)
            {
                // only the first thread to get here will initialize this state
                if (!_waitForProcessStartTaskSource.Task.IsCompleted)
                {
                    loggers = _loggers.ToArray();
                    _startTime = DateTime.Now;
                    _waitForProcessStartTaskSource.SetResult(_p);
                }
            }

            // only the first thread that entered the lock above will run this
            if (loggers != null)
            {
                foreach (IProcessLogger logger in loggers)
                {
                    logger.ProcessStarted(this);
                }
            }

            return this;
        }

        private void ReadStreamToLoggers(StreamReader reader, ProcessStream stream, CancellationToken cancelToken)
        {
            IProcessLogger[] loggers = Loggers;

            const int BufferSize = 2048;
            using IMemoryOwner<byte> memOwner = MemoryPool<byte>.Shared.Rent(BufferSize);

            do
            {
                Span<char> buffer = MemoryMarshal.Cast<byte, char>(memOwner.Memory.Span.Slice(0, BufferSize));
                int charsRead = reader.Read(buffer);

                buffer = buffer[0..charsRead];

                // this lock keeps the standard out/error streams from being intermixed
                lock (Loggers)
                {
                    while (!buffer.IsEmpty)
                    {
                        string line;
                        int index = buffer.IndexOf('\n');
                        // If no newline, just write the buffer sans the trailing \r.
                        if (index == -1)
                        {
                            buffer = buffer[^1] == '\r'
                                ? buffer[0..^1]
                                : buffer;
                            line = new string(buffer);
                            foreach (IProcessLogger logger in loggers)
                            {
                                logger.Write(this, line, stream);
                            }
                            break;
                        }

                        // If we start with a new line, flush the current line.
                        if (index == 0)
                        {
                            line = string.Empty;
                        }
                        else
                        {
                            // Otherwise find the \n and emit the partial buffer.
                            Span<char> charSeq = buffer[index - 1] == '\r'
                                ? buffer[0..(index - 1)]
                                : buffer[0..index];
                            line = new string(charSeq);
                        }

                        buffer = buffer.Slice(index + 1);

                        foreach (IProcessLogger logger in loggers)
                        {
                            logger.WriteLine(this, line, stream);
                        }
                    }
                }
            }
            while (!reader.EndOfStream && !cancelToken.IsCancellationRequested);
        }

        public void Kill(KillReason reason = KillReason.Unknown)
        {
            IProcessLogger[] loggers = null;
            Process p = null;
            lock (_lock)
            {
                if (_waitForExitTask.IsCompleted)
                {
                    return;
                }
                if (_killReason.HasValue)
                {
                    return;
                }
                _killReason = reason;
                if (!_p.HasExited)
                {
                    p = _p;
                }

                loggers = _loggers.ToArray();
                _cancelSource.Cancel();
            }

            if (p != null)
            {
                // its possible the process could exit just after we check so
                // we still have to handle the InvalidOperationException that
                // can be thrown.
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
            }

            foreach (IProcessLogger logger in loggers)
            {
                logger.ProcessKilled(this, reason);
            }
        }

        private async Task<int> InternalWaitForExit(Task<Process> startProcessTask, Task stdOutTask, Task stdErrTask)
        {
            DebugTrace("starting InternalWaitForExit");
            Process p = await startProcessTask.ConfigureAwait(false);
            DebugTrace("InternalWaitForExit {0} '{1}'", p.Id, _replayCommand);

            DebugTrace("awaiting process {0} exit", p.Id);
            await p.WaitForExitAsync().ConfigureAwait(false);
            DebugTrace("process {0} completed with exit code {1}", p.Id, p.ExitCode);

            DebugTrace("awaiting to flush stdOut and stdErr for process {0} for up to 15 seconds", p.Id);

            Task streamsTask = Task.WhenAll(stdOutTask, stdErrTask);

            streamsTask = streamsTask.ContinueWith(
                t => DebugTrace(t.Exception.ToString()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            Task completedTask = await Task.WhenAny(
                    streamsTask,
                    Task.Delay(TimeSpan.FromSeconds(15)))
                .ConfigureAwait(false);

            if (completedTask != streamsTask)
            {
                DebugTrace("WARNING: Flushing stdOut and stdErr for process {0} timed out, threads used for the tasks might be leaked", p.Id);
            }
            else
            {
                DebugTrace("Flushed stdOut and stdErr for process {0}", p.Id);
            }

            foreach (IProcessLogger logger in Loggers)
            {
                logger.ProcessExited(this);
            }

            lock (_lock)
            {
                if (_expectedExitCode.HasValue && p.ExitCode != _expectedExitCode.Value)
                {
                    throw new Exception("Process returned exit code " + p.ExitCode + ", expected " + _expectedExitCode.Value + Environment.NewLine +
                                        "Command Line: " + ReplayCommand + Environment.NewLine +
                                        "Working Directory: " + WorkingDirectory);
                }
                DebugTrace("InternalWaitForExit {0} returning {1}", p.Id, p.ExitCode);
                return p.ExitCode;
            }
        }

        private void DebugTrace(string format, params object[] args)
        {
            lock (_lock)
            {
                if (_traceOutput != null)
                {
                    string message = string.Format(format, args);
                    _traceOutput.WriteLine("TRACE: {0}", message);
                }
            }
        }

        private sealed class ConsoleTestOutputHelper : ITestOutputHelper
        {
            private readonly ITestOutputHelper _output;

            public ConsoleTestOutputHelper(ITestOutputHelper output)
            {
                _output = output;
            }

            public void WriteLine(string message)
            {
                Console.WriteLine(message);
                _output?.WriteLine(message);

            }

            public void WriteLine(string format, params object[] args)
            {
                Console.WriteLine(format, args);
                _output?.WriteLine(format, args);
            }
        }
    }
}
