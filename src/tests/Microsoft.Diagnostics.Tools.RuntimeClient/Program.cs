// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tracing.Tests.Common;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.Tests
{
    class Program
    {
        static int Main(string[] args)
        {
            SendSmallerHeaderCommand();
            SendInvalidDiagnosticMessageTypeCommand();
            SendInvalidInputData();
            TestStartEventPipeTracing();
            TestCollectEventPipeTracing();
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
                        bw.Write((uint)DiagnosticMessageType.StartEventPipeTracing);
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

        private static byte[] Serialize(MessageHeader header, TestSessionConfiguration configuration, Stream stream)
        {
            using (var bw = new BinaryWriter(stream))
            {
                bw.Write((uint)header.RequestType);
                bw.Write(header.Pid);

                bw.Write(configuration.CircularBufferSizeInMB);

                bw.WriteString(null);

                bw.Write(configuration.Providers.Count());
                foreach (var provider in configuration.Providers)
                {
                    bw.Write(provider.Keywords);
                    bw.Write((uint)provider.EventLevel);

                    bw.WriteString(provider.Name);
                    bw.WriteString(provider.FilterData);
                }

                bw.Flush();
                stream.Position = 0;

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }

        }

        private static void SendInvalidInputData()
        {
            var configs = new TestSessionConfiguration[] {
                new TestSessionConfiguration {
                    CircularBufferSizeInMB = 0,
                    TestName = "0 size circular buffer",
                    Providers = new TestProvider[] {
                        new TestProvider{
                            Name = "Microsoft-Windows-DotNETRuntime"
                        },
                    },
                },
                new TestSessionConfiguration {
                    CircularBufferSizeInMB = 64,
                    TestName = "null providers",
                    Providers = null,
                },
                new TestSessionConfiguration {
                    CircularBufferSizeInMB = 64,
                    TestName = "no providers",
                    Providers = new TestProvider[]{ },
                },
                new TestSessionConfiguration {
                    CircularBufferSizeInMB = 64,
                    TestName = "null provider name",
                    Providers = new TestProvider[]{ new TestProvider { Name = null, }, },
                },
                new TestSessionConfiguration {
                    CircularBufferSizeInMB = 64,
                    TestName = "empty provider name",
                    Providers = new TestProvider[]{ new TestProvider { Name = string.Empty, }, },
                },
                new TestSessionConfiguration {
                    CircularBufferSizeInMB = 64,
                    TestName = "white space provider name",
                    Providers = new TestProvider[]{ new TestProvider { Name = " ", }, },
                },
            };

            foreach (var config in configs)
            {
                ulong sessionId = 0;

                try
                {
                    var header = new MessageHeader {
                        RequestType = DiagnosticMessageType.CollectEventPipeTracing,
                        Pid = (uint)Process.GetCurrentProcess().Id,
                    };

                    byte[] bytes;
                    using (var stream = new MemoryStream())
                        bytes = Serialize(header, config, stream);

                    Console.WriteLine($"Test: {config.TestName}");
                    sessionId = EventPipeClient.SendCommand(ThisProcess.Id, bytes);

                    // Check that a session was created.
                    Assert.Equal("EventPipe Session Id", sessionId, (ulong)0);
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
        }

        private static void SendInvalidPayloadToCollectCommand()
        {
            Console.WriteLine("Send Invalid Payload To Collect Command.");

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

                var configuration = new SessionConfiguration(circularBufferSizeMB, filePath, providers);

                // Start session #1.
                sessionId = EventPipeClient.StartTracingToFile(
                    processId: ThisProcess.Id,
                    configuration: configuration);

                // Check that a session was created.
                Assert.Equal("EventPipe Session Id", sessionId, (ulong)0);
            }
            finally
            {
                if (sessionId != 0)
                    EventPipeClient.StopTracing(ThisProcess.Id, sessionId);
            }
        }

        private static void TestStartEventPipeTracing()
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

                var configuration = new SessionConfiguration(circularBufferSizeMB, filePath, providers);

                // Start session.
                sessionId = EventPipeClient.StartTracingToFile(
                    processId: ThisProcess.Id,
                    configuration: configuration);

                // Check that a session was created.
                Assert.NotEqual("EventPipe Session Id", sessionId, (ulong)0);

                // Check that file is created
                // NOTE: This might change in the future, and file could be created only "OnDisable".
                Assert.Equal("EventPipe output file", File.Exists(filePath), true);

                { // Attempt to create another session, and verify that is not possible.
                    var sessionId2 = EventPipeClient.StartTracingToFile(
                        processId: ThisProcess.Id,
                        configuration: configuration);

                    // Check that a new session was not created.
                    Assert.Equal("EventPipe Session Id", sessionId2, (ulong)0);
                }

                Workload.DoWork(10);

                var ret = EventPipeClient.StopTracing(ThisProcess.Id, sessionId);
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
                    EventPipeClient.StopTracing(ThisProcess.Id, sessionId);
            }
        }

        private static void TestCollectEventPipeTracing()
        {
            Console.WriteLine("Start collecting.");

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

                var configuration = new SessionConfiguration(circularBufferSizeMB, filePath, providers);

                // Start session.
                using (Stream stream = EventPipeClient.CollectTracing(
                    processId: ThisProcess.Id,
                    configuration: configuration,
                    sessionId: out sessionId))
                {
                    var collectingTask = new Task(() => {
                        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                        {
                            while (true)
                            {
                                var buffer = new byte[16 * 1024];
                                int nBytesRead = stream.Read(buffer, 0, buffer.Length);
                                if (nBytesRead <= 0)
                                    break;
                                fs.Write(buffer, 0, nBytesRead);
                            }
                        }
                    });
                    collectingTask.Start();

                    // Check that a session was created.
                    Assert.NotEqual("EventPipe Session Id", sessionId, (ulong)0);

                    // Check that file is created
                    // NOTE: This might change in the future, and file could be created only "OnDisable".
                    Assert.Equal("EventPipe output file", File.Exists(filePath), true);

                    { // Attempt to create another session, and verify that is not possible.
                        EventPipeClient.CollectTracing(
                             processId: ThisProcess.Id,
                             configuration: configuration,
                             sessionId: out var sessionId2);

                        // Check that a new session was not created.
                        Assert.Equal("EventPipe Session Id", sessionId2, (ulong)0);
                    }

                    Workload.DoWork(10);

                    var ret = EventPipeClient.StopTracing(ThisProcess.Id, sessionId);
                    Assert.Equal("Expect return value to be the disabled session Id", sessionId, ret);
                    sessionId = 0; // Reset session Id, we do not need to disable it later.

                    // Check file is valid.
                    ValidateNetPerf(filePath);
                }
            }
            finally
            {
                if (sessionId != 0)
                    EventPipeClient.StopTracing(ThisProcess.Id, sessionId);
            }
        }

        private static void ValidateNetPerf(string filePath)
        {
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

        [Conditional("DEBUG")]
        private static void DumpNetPerf(string filePath)
        {
            using (var trace = new TraceLog(TraceLog.CreateFromEventPipeDataFile(filePath)).Events.GetSource())
            {
                trace.Dynamic.All += (TraceEvent e) => {
                    if (!string.IsNullOrWhiteSpace(e.ProviderName) && !string.IsNullOrWhiteSpace(e.EventName))
                    {
                        Debug.WriteLine($"Event Provider: {e.ProviderName}");
                        Debug.WriteLine($"    Event Name: {e.EventName}");
                    }
                };
                trace.Process();
            }
        }
    }
}
