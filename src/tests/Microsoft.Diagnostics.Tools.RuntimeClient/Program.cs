// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using System;
using System.Diagnostics;
using System.IO;
using Tracing.Tests.Common;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.Tests
{
    class Program
    {
        static int Main(string[] args)
        {
            SendSmallerHeaderCommand();
            SendInvalidDiagnosticMessageTypeCommand();

            StartNewTracingToFileSession();
            StartNewTracingToStreamSession();
            return 100;
        }

        private static Process ThisProcess { get; } = Process.GetCurrentProcess();

        private static void SendSmallerHeaderCommand()
        {
            Console.WriteLine("Send a small payload as header.");

            ulong sessionId = 0;

            try
            {
                byte[] bytes;
                using (var stream = new MemoryStream())
                {
                    using (var bw = new BinaryWriter(stream))
                    {
                        bw.Write((uint)DiagnosticMessageType.StartSession);
                        bw.Flush();
                        stream.Position = 0;

                        bytes = new byte[stream.Length];
                        stream.Read(bytes, 0, bytes.Length);
                    }
                }

                sessionId = EventPipeClient.SendCommand(ThisProcess.Id, bytes);
            }
            catch (EndOfStreamException)
            {
                Assert.Equal("EventPipe Session Id", sessionId, (ulong)0);
            }
            catch
            {
                Assert.True("Send command threw unexpected exception", false);
            }
        }

        private static void SendInvalidDiagnosticMessageTypeCommand()
        {
            Console.WriteLine("Send a wrong message type as the diagnostic header header.");
            ulong sessionId = 0;

            try
            {
                byte[] bytes;
                using (var stream = new MemoryStream())
                {
                    using (var bw = new BinaryWriter(stream))
                    {
                        bw.Write(uint.MaxValue);
                        bw.Write(ThisProcess.Id);
                        bw.Flush();
                        stream.Position = 0;

                        bytes = new byte[stream.Length];
                        stream.Read(bytes, 0, bytes.Length);
                    }
                }

                sessionId = EventPipeClient.SendCommand(ThisProcess.Id, bytes);
            }
            catch (EndOfStreamException)
            {
                Assert.Equal("EventPipe Session Id", sessionId, (ulong)0);
            }
            catch
            {
                Assert.True("Send command threw unexpected exception", false);
            }
        }

        private static void StartNewTracingToFileSession()
        {
            Console.WriteLine("Start collection.");

            ulong sessionId = 0;

            try
            {
                uint circularBufferSizeMB = 64;
                var filePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    $"dotnetcore-eventpipe-{ThisProcess.Id}.netperf");
                var providers = new[] {
                    new Provider(name: "Microsoft-Windows-DotNETRuntime"),
                };
                // "Microsoft-Windows-DotNETRuntime:0x00000004C14FCCBD:4"

                var configuration = new SessionConfiguration(circularBufferSizeMB, filePath, providers);

                // Start session.
                sessionId = EventPipeClient.EnableTracingToFile(
                    processId: ThisProcess.Id,
                    configuration: configuration);

                // Check that a session was created.
                Assert.NotEqual("EventPipe Session Id", sessionId, (ulong)0);

                // Check that file is created
                // NOTE: This might change in the future, and file could be created only "OnDisable".
                Assert.Equal("EventPipe output file", File.Exists(filePath), true);

                {
                    // Attempt to create another session, and verify that is not possible.
                    var sessionId2 = EventPipeClient.EnableTracingToFile(
                        processId: ThisProcess.Id,
                        configuration: configuration);

                    // Check that a new session was not created.
                    Assert.Equal("EventPipe Session Id", sessionId2, (ulong)0);
                }

                Workload.DoWork(10);

                var ret = EventPipeClient.DisableTracingToFile(ThisProcess.Id, sessionId);
                Assert.Equal("Expect return value to be the disabled session Id", sessionId, ret);
                sessionId = 0; // Reset session Id, we do not need to disable it later.

                // Check file is valid.
                var nEventPipeResults = 0;
                using (var trace = new TraceLog(TraceLog.CreateFromEventPipeDataFile(filePath)).Events.GetSource())
                {
                    trace.Dynamic.All += (TraceEvent data) => {
                        ++nEventPipeResults;
                    };
                    trace.Process();
                }

                // Assert there were events in the file.
                Assert.NotEqual("Found events in trace file", nEventPipeResults, 0);
            }
            finally
            {
                if (sessionId != 0)
                    EventPipeClient.DisableTracingToFile(ThisProcess.Id, sessionId);
            }
        }

        private static void StartNewTracingToStreamSession()
        {
            Console.WriteLine("Start streaming.");
        }
    }
}
