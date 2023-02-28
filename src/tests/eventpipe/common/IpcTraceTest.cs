// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace EventPipe.UnitTests.Common
{
    public class Logger
    {
        public static Logger logger = new Logger();
        private TextWriter _log;
        private Stopwatch _sw;
        public Logger(TextWriter log = null)
        {
            _log = log ?? Console.Out;
            _sw = new Stopwatch();
        }

        public void Log(string message)
        {
            if (!_sw.IsRunning)
            {
                _sw.Start();
            }

            _log.WriteLine($"{_sw.Elapsed.TotalSeconds,5:f1}s: {message}");
        }
    }

    public class ExpectedEventCount
    {
        // The acceptable percent error on the expected value
        // represented as a floating point value in [0,1].
        public float Error { get; private set; }

        // The expected count of events. A value of -1 indicates
        // that count does not matter, and we are simply testing
        // that the provider exists in the trace.
        public int Count { get; private set; }

        public ExpectedEventCount(int count, float error = 0.0f)
        {
            Count = count;
            Error = error;
        }

        public bool Validate(int actualValue)
        {
            return Count == -1 || CheckErrorBounds(actualValue);
        }

        public bool CheckErrorBounds(int actualValue)
        {
            return Math.Abs(actualValue - Count) <= (Count * Error);
        }

        public static implicit operator ExpectedEventCount(int i)
        {
            return new ExpectedEventCount(i);
        }

        public override string ToString()
        {
            return $"{Count} +- {Count * Error}";
        }
    }

    // This event source is used by the test infra to
    // to insure that providers have finished being enabled
    // for the session being observed. Since the client API
    // returns the pipe for reading _before_ it finishes
    // enabling the providers to write to that session,
    // we need to guarantee that our providers are on before
    // sending events. This is a _unique_ problem I imagine
    // should _only_ affect scenarios like these tests
    // where the reading and sending of events are required
    // to synchronize.
    public sealed class SentinelEventSource : EventSource
    {
        private SentinelEventSource() {}
        public static SentinelEventSource Log = new SentinelEventSource();
        public void SentinelEvent() { WriteEvent(1, "SentinelEvent"); }
    }

    public class IpcTraceTest
    {
        // This Action is executed while the trace is being collected.
        private Action _eventGeneratingAction;

        // A dictionary of event providers to number of events.
        // A count of -1 indicates that you are only testing for the presence of the provider
        // and don't care about the number of events sent
        private Dictionary<string, ExpectedEventCount> _expectedEventCounts;
        private Dictionary<string, int> _actualEventCounts = new Dictionary<string, int>();
        private int _droppedEvents = 0;

        // A function to be called with the EventPipeEventSource _before_
        // the call to `source.Process()`.  The function should return another
        // function that will be called to check whether the optional test was validated.
        // Example in situ: providervalidation.cs
        private Func<EventPipeEventSource, Func<int>> _optionalTraceValidator;

        /// <summary>
        /// This is list of the EventPipe providers to turn on for the test execution
        /// </summary>
        private List<EventPipeProvider> _testProviders;

        /// <summary>
        /// This represents the current EventPipeSession 
        /// </summary>
        private EventPipeSession _eventPipeSession;

        /// <summary>
        /// This is the list of EventPipe providers for the sentinel EventSource that indicates that the process is ready
        /// </summary>
        private List<EventPipeProvider> _sentinelProviders = new List<EventPipeProvider>()
        {
            new EventPipeProvider("SentinelEventSource", EventLevel.Verbose, -1)
        };

        private IpcTraceTest(
            Dictionary<string, ExpectedEventCount> expectedEventCounts,
            Action eventGeneratingAction,
            List<EventPipeProvider> providers,
            int circularBufferMB,
            Func<EventPipeEventSource, Func<int>> optionalTraceValidator = null)
        {
            _eventGeneratingAction = eventGeneratingAction;
            _expectedEventCounts = expectedEventCounts;
            _testProviders = providers;
            _optionalTraceValidator = optionalTraceValidator;
        }

        private int Fail(string message = "")
        {
            Logger.logger.Log("Test FAILED!");
            Logger.logger.Log(message);
            Logger.logger.Log("Configuration:");
            Logger.logger.Log("{");
            Logger.logger.Log("\tproviders: [");
            Logger.logger.Log("\t]");
            Logger.logger.Log("}\n");
            Logger.logger.Log("Expected:");
            Logger.logger.Log("{");
            foreach (var (k, v) in _expectedEventCounts)
            {
                Logger.logger.Log($"\t\"{k}\" = {v}");
            }
            Logger.logger.Log("}\n");

            Logger.logger.Log("Actual:");
            Logger.logger.Log("{");
            foreach (var (k, v) in _actualEventCounts)
            {
                Logger.logger.Log($"\t\"{k}\" = {v}");
            }
            Logger.logger.Log("}");

            return -1;
        }

        private int Validate()
        {
            var isClean = EnsureCleanEnvironment();
            if (!isClean)
            {
                return -1;
            }
            // CollectTracing returns before EventPipe::Enable has returned, so the
            // the sources we want to listen for may not have been enabled yet.
            // We'll use this sentinel EventSource to check if Enable has finished
            ManualResetEvent sentinelEventReceived = new ManualResetEvent(false);
            var sentinelTask = new Task(() =>
            {
                Logger.logger.Log("Started sending sentinel events...");
                while (!sentinelEventReceived.WaitOne(50))
                {
                    SentinelEventSource.Log.SentinelEvent();
                }
                Logger.logger.Log("Stopped sending sentinel events");
            });
            sentinelTask.Start();

            int processId = Environment.ProcessId;
            object threadSync = new object(); // for locking eventpipeSession access
            Func<int> optionalTraceValidationCallback = null;
            DiagnosticsClient client = new DiagnosticsClient(processId);
            var readerTask = new Task(() =>
            {
                Logger.logger.Log("Connecting to EventPipe...");
                try
                {
                    _eventPipeSession = client.StartEventPipeSession(_testProviders.Concat(_sentinelProviders));
                }
                catch(DiagnosticsClientException ex)
                {
                    Logger.logger.Log("Failed to connect to EventPipe!");
                    Logger.logger.Log(ex.ToString());
                    throw new ApplicationException("Failed to connect to EventPipe");
                }

                using var eventPipeStream = new StreamProxy(_eventPipeSession.EventStream);
                Logger.logger.Log("Creating EventPipeEventSource...");
                using EventPipeEventSource source = new EventPipeEventSource(eventPipeStream);
                Logger.logger.Log("EventPipeEventSource created");

                source.Dynamic.All += (eventData) =>
                {
                    try
                    {
                        if (eventData.ProviderName == "SentinelEventSource")
                        {
                            if (!sentinelEventReceived.WaitOne(0))
                            {
                                Logger.logger.Log("Saw sentinel event");
                            }

                            sentinelEventReceived.Set();
                        }

                        else if (_actualEventCounts.TryGetValue(eventData.ProviderName, out _))
                        {
                            _actualEventCounts[eventData.ProviderName]++;
                        }
                        else
                        {
                            Logger.logger.Log($"Saw new provider '{eventData.ProviderName}'");
                            _actualEventCounts[eventData.ProviderName] = 1;
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.logger.Log("Exception in Dynamic.All callback " + e.ToString());
                    }
                };
                Logger.logger.Log("Dynamic.All callback registered");

                if (_optionalTraceValidator != null)
                {
                    Logger.logger.Log("Running optional trace validator");
                    optionalTraceValidationCallback = _optionalTraceValidator(source);
                    Logger.logger.Log("Finished running optional trace validator");
                }

                Logger.logger.Log("Starting stream processing...");
                try
                {
                    source.Process();
                    _droppedEvents = source.EventsLost;
                }
                catch (Exception)
                {
                    Logger.logger.Log($"Exception thrown while reading; dumping culprit stream to disk...");
                    eventPipeStream.DumpStreamToDisk();
                    // rethrow it to fail the test
                    throw;
                }
                Logger.logger.Log("Stopping stream processing");
                Logger.logger.Log($"Dropped {source.EventsLost} events");
            });

            readerTask.Start();
            sentinelEventReceived.WaitOne();

            Logger.logger.Log("Starting event generating action...");
            _eventGeneratingAction();
            Logger.logger.Log("Stopping event generating action");

            // Should throw if the reader task throws any exceptions
            var tokenSource = new CancellationTokenSource();
            CancellationToken ct = tokenSource.Token;
            readerTask.ContinueWith((task) =>
            {
                // if our reader task died earlier, we need to break the infinite wait below.
                // We'll allow the AggregateException to be thrown and fail the test though.
                Logger.logger.Log($"Task stats: isFaulted: {task.IsFaulted}, Exception == null: {task.Exception == null}");
                if (task.IsFaulted || task.Exception != null)
                {
                    tokenSource.Cancel();
                }

                return task;
            });

            var stopTask = Task.Run(() => 
            {
                Logger.logger.Log("Sending StopTracing command...");
                lock (threadSync) // eventpipeSession
                {
                    _eventPipeSession.Stop();
                }
                Logger.logger.Log("Finished StopTracing command");
            }, ct);

            try
            {
                Task.WaitAll(new Task[] { readerTask, stopTask }, ct);
            }
            catch (OperationCanceledException)
            {
                Logger.logger.Log($"A task faulted");
                Logger.logger.Log($"\treaderTask.IsFaulted = {readerTask.IsFaulted}");
                if (readerTask.Exception != null)
                {
                    throw readerTask.Exception;
                }
                return -1;
            }

            Logger.logger.Log("Reader task finished");
            Logger.logger.Log($"Dropped {_droppedEvents} events");

            foreach (var (provider, expectedCount) in _expectedEventCounts)
            {
                if (_actualEventCounts.TryGetValue(provider, out var actualCount))
                {
                    if (!expectedCount.Validate(actualCount))
                    {
                        return Fail($"Event count mismatch for provider \"{provider}\": expected {expectedCount}, but saw {actualCount}");
                    }
                }
                else
                {
                    return Fail($"No events for provider \"{provider}\"");
                }
            }

            if (optionalTraceValidationCallback != null)
            {
                Logger.logger.Log("Validating optional callback...");
                // reader thread should be dead now, no need to lock
                return optionalTraceValidationCallback();
            }
            else
            {
                return 100;
            }
        }

        // Ensure that we have a clean environment for running the test.
        // Specifically check that we don't have more than one match for 
        // Diagnostic IPC sockets in the TempPath.  These can be left behind
        // by bugs, catastrophic test failures, etc. from previous testing.
        // The tmp directory is only cleared on reboot, so it is possible to
        // run into these zombie pipes if there are failures over time.
        // Note: Windows has some guarantees about named pipes not living longer
        // the process that created them, so we don't need to check on that platform.
        private bool EnsureCleanEnvironment()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Logger.logger.Log("Validating clean environment...");
                // mimic the RuntimeClient's code for finding OS Transports
                IEnumerable<FileInfo> ipcPorts = Directory.GetFiles(Path.GetTempPath())
                    .Select(namedPipe => new FileInfo(namedPipe))
                    .Where(input => Regex.IsMatch(input.Name, $"^dotnet-diagnostic-{Environment.ProcessId}-(\\d+)-socket$"));
                
                if (ipcPorts.Count() > 1)
                {
                    Logger.logger.Log($"Found {ipcPorts.Count()} OS transports for pid {Environment.ProcessId}:");
                    foreach (var match in ipcPorts)
                    {
                        Logger.logger.Log($"\t{match.Name}");
                    }

                    // Get everything _except_ the newest pipe
                    var duplicates = ipcPorts.OrderBy(fileInfo => fileInfo.CreationTime.Ticks).SkipLast(1);
                    foreach (var duplicate in duplicates)
                    {
                        Logger.logger.Log($"Attempting to delete the oldest pipe: {duplicate.FullName}");
                        duplicate.Delete(); // should throw if we can't delete and be caught in Validate
                        Logger.logger.Log($"Deleted");
                    }

                    var afterIpcPorts = Directory.GetFiles(Path.GetTempPath())
                        .Select(namedPipe => new FileInfo(namedPipe))
                        .Where(input => Regex.IsMatch(input.Name, $"^dotnet-diagnostic-{Environment.ProcessId}-(\\d+)-socket$"));

                    if (afterIpcPorts.Count() == 1)
                    {
                        return true;
                    }
                    else
                    {
                        Logger.logger.Log($"Unable to clean the environment.  The following transports are on the system:");
                        foreach(var transport in afterIpcPorts)
                        {
                            Logger.logger.Log($"\t{transport.FullName}");
                        }
                        return false;
                    }
                }
                Logger.logger.Log("Environment was clean.");
                return true;
            }

            return true;
        }

        public static int RunAndValidateEventCounts(
            Dictionary<string, ExpectedEventCount> expectedEventCounts,
            Action eventGeneratingAction,
            List<EventPipeProvider> providers,
            int circularBufferMB=1024,
            Func<EventPipeEventSource, Func<int>> optionalTraceValidator = null)
        {
            Logger.logger.Log("==TEST STARTING==");
            var test = new IpcTraceTest(expectedEventCounts, eventGeneratingAction, providers, circularBufferMB, optionalTraceValidator);
            var ret = test.Validate();
            if (ret == 100)
            {
                Logger.logger.Log("==TEST FINISHED: PASSED!==");
            }
            else
            {
                Logger.logger.Log("==TEST FINISHED: FAILED!==");
            }

            return ret;
        }
    }
}