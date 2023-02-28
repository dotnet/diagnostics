// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class TestOutputProcessLogger : IProcessLogger
    {
        private static readonly string _timeFormat = "mm\\:ss\\.fff";
        private readonly ITestOutputHelper _output;
        private readonly StringBuilder[] _lineBuffers;

        public TestOutputProcessLogger(ITestOutputHelper output)
        {
            _output = output;
            _lineBuffers = new StringBuilder[(int)ProcessStream.MaxStreams];
        }

        public void ProcessStarted(ProcessRunner runner)
        {
            lock (this)
            {
                _output.WriteLine("Running Process: " + runner.ReplayCommand);
                _output.WriteLine("Working Directory: " + runner.WorkingDirectory);
                IEnumerable<KeyValuePair<string,string>> additionalEnvVars =
                    runner.EnvironmentVariables.Where(kv => Environment.GetEnvironmentVariable(kv.Key) != kv.Value);

                if(additionalEnvVars.Any())
                {
                    _output.WriteLine("Additional Environment Variables: " +
                        string.Join(", ", additionalEnvVars.Select(kv => kv.Key + "=" + kv.Value)));
                }
                _output.WriteLine("{");
            }
        }

        public virtual void Write(ProcessRunner runner, string data, ProcessStream stream)
        {
            lock (this)
            {
                AppendToLineBuffer(runner, stream, data);
            }
        }

        public virtual void WriteLine(ProcessRunner runner, string data, ProcessStream stream)
        {
            lock (this)
            {
                StringBuilder lineBuffer = AppendToLineBuffer(runner, stream, data);
                //Ensure all output is written even if it isn't a full line before we log input
                if (stream == ProcessStream.StandardIn)
                {
                    FlushOutput();
                }
                _output.WriteLine(lineBuffer.ToString());
                _lineBuffers[(int)stream] = null;
            }
        }

        public virtual void ProcessExited(ProcessRunner runner)
        {
            lock (this)
            {
                TimeSpan offset = runner.StartTime - DateTime.Now;
                _output.WriteLine("}");
                _output.WriteLine($"Process {runner.ProcessId} exited {runner.ExitCode} ({offset.ToString(_timeFormat)} elapsed)");
                _output.WriteLine("");
            }
        }

        public void ProcessKilled(ProcessRunner runner, KillReason reason)
        {
            lock (this)
            {
                TimeSpan offset = runner.StartTime - DateTime.Now;
                string reasonText = reason switch
                {
                    KillReason.TimedOut => "Process timed out",
                    KillReason.Stopped => "Process was stopped",
                    KillReason.Unknown => "Kill() was called",
                    _ => "Reason Unknown"
                };
                _output.WriteLine($"Killing process {runner.ProcessId}: {offset.ToString(_timeFormat)} - {reasonText}");
            }
        }

        protected void FlushOutput()
        {
            if (_lineBuffers[(int)ProcessStream.StandardOut] != null)
            {
                _output.WriteLine(_lineBuffers[(int)ProcessStream.StandardOut].ToString());
                _lineBuffers[(int)ProcessStream.StandardOut] = null;
            }
            if (_lineBuffers[(int)ProcessStream.StandardError] != null)
            {
                _output.WriteLine(_lineBuffers[(int)ProcessStream.StandardError].ToString());
                _lineBuffers[(int)ProcessStream.StandardError] = null;
            }
        }

        private StringBuilder AppendToLineBuffer(ProcessRunner runner, ProcessStream stream, string data)
        {
            StringBuilder lineBuffer = _lineBuffers[(int)stream];
            if (lineBuffer == null)
            {
                TimeSpan offset = runner.StartTime - DateTime.Now;
                lineBuffer = new StringBuilder();
                lineBuffer.Append("    ");
                if (stream == ProcessStream.StandardError)
                {
                    lineBuffer.Append("STDERROR: ");
                }
                else if (stream == ProcessStream.StandardIn)
                {
                    lineBuffer.Append("STDIN: ");
                }
                lineBuffer.Append(offset.ToString(_timeFormat));
                lineBuffer.Append(": ");
                _lineBuffers[(int)stream] = lineBuffer;
            }

            // xunit has a bug where a non-printable character isn't properly escaped when
            // it is written into the xml results which ultimately results in
            // the xml being improperly truncated. For example MDbg has a test case that prints
            // \0 and dotnet tools print \u001B to colorize their console output.
            foreach(char c in data)
            {
                if(!char.IsControl(c))
                {
                    lineBuffer.Append(c);
                }
            }
            return lineBuffer;
        }
    }
}