// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Graphs;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    public static class EventPipeDotNetHeapDumper
    {
        internal static volatile bool eventPipeDataPresent;
        internal static volatile bool dumpComplete;

        /// <summary>
        /// Given a nettrace file from a EventPipe session with the appropriate provider and keywords turned on,
        /// generate a GCHeapDump using the resulting events.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="memoryGraph"></param>
        /// <param name="log"></param>
        /// <param name="dotNetInfo"></param>
        /// <returns></returns>
        public static bool DumpFromEventPipeFile(string path, MemoryGraph memoryGraph, TextWriter log, DotNetHeapInfo dotNetInfo)
        {
            DateTime start = DateTime.Now;
            Func<TimeSpan> getElapsed = () => DateTime.Now - start;

            DotNetHeapDumpGraphReader dumper = new(log)
            {
                DotNetHeapInfo = dotNetInfo
            };

            try
            {
                TimeSpan lastEventPipeUpdate = getElapsed();

                int gcNum = -1;

                EventPipeEventSource source = new(path);

                source.Clr.GCStart += delegate (GCStartTraceData data)
                {
                    eventPipeDataPresent = true;

                    if (gcNum < 0 && data.Depth == 2 && data.Type != GCType.BackgroundGC)
                    {
                        gcNum = data.Count;
                        log.WriteLine("{0,5:n1}s: .NET Dump Started...", getElapsed().TotalSeconds);
                    }
                };

                source.Clr.GCStop += delegate (GCEndTraceData data)
                {
                    if (data.Count == gcNum)
                    {
                        log.WriteLine("{0,5:n1}s: .NET GC Complete.", getElapsed().TotalSeconds);
                        dumpComplete = true;
                    }
                };

                source.Clr.GCBulkNode += delegate (GCBulkNodeTraceData data)
                {
                    eventPipeDataPresent = true;

                    if ((getElapsed() - lastEventPipeUpdate).TotalMilliseconds > 500)
                    {
                        log.WriteLine("{0,5:n1}s: Making GC Heap Progress...", getElapsed().TotalSeconds);
                    }

                    lastEventPipeUpdate = getElapsed();
                };

                if (memoryGraph != null)
                {
                    dumper.SetupCallbacks(memoryGraph, source);
                }

                log.WriteLine("{0,5:n1}s: Starting to process events", getElapsed().TotalSeconds);
                source.Process();
                log.WriteLine("{0,5:n1}s: Finished processing events", getElapsed().TotalSeconds);

                if (eventPipeDataPresent)
                {
                    dumper.ConvertHeapDataToGraph();
                }
            }
            catch (Exception e)
            {
                log.WriteLine($"{getElapsed().TotalSeconds,5:n1}s: [Error] Exception processing events: {e}");
            }

            log.WriteLine("[{0,5:n1}s: Done Dumping .NET heap success={1}]", getElapsed().TotalSeconds, dumpComplete);

            return dumpComplete;
        }

        /// <summary>
        /// Given a factory for creating an EventPipe session with the appropriate provider and keywords turned on,
        /// generate a GCHeapDump using the resulting events.  The correct keywords and provider name
        /// are given as input to the Func eventPipeEventSourceFactory.
        /// </summary>
        /// <param name="processId"></param>
        /// <param name="diagnosticPort"></param>
        /// <param name="memoryGraph"></param>
        /// <param name="log"></param>
        /// <param name="timeout"></param>
        /// <param name="dotNetInfo"></param>
        /// <returns></returns>
        public static bool DumpFromEventPipe(CancellationToken ct, int processId, string diagnosticPort, MemoryGraph memoryGraph, TextWriter log, int timeout, DotNetHeapInfo dotNetInfo)
        {
            DateTime start = DateTime.Now;
            Func<TimeSpan> getElapsed = () => DateTime.Now - start;

            DotNetHeapDumpGraphReader dumper = new(log)
            {
                DotNetHeapInfo = dotNetInfo
            };

            try
            {
                TimeSpan lastEventPipeUpdate = getElapsed();
                bool fDone = false;
                log.WriteLine("{0,5:n1}s: Creating type table flushing task", getElapsed().TotalSeconds);

                using (EventPipeSessionController typeFlushSession = new(processId, diagnosticPort, new List<EventPipeProvider> {
                    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational)
                }, false))
                {
                    log.WriteLine("{0,5:n1}s: Flushing the type table", getElapsed().TotalSeconds);
                    typeFlushSession.Source.AllEvents += (traceEvent) => {
                        if (!fDone)
                        {
                            fDone = true;
                            Task.Run(
                                () => {
                                    typeFlushSession.EndSession();
                                    typeFlushSession.Source.StopProcessing();
                                });
                        }
                    };

                    typeFlushSession.Source.Process();
                    log.WriteLine("{0,5:n1}s: Done flushing the type table", getElapsed().TotalSeconds);
                }


                // Start the providers and trigger the GCs.
                log.WriteLine("{0,5:n1}s: Requesting a .NET Heap Dump", getElapsed().TotalSeconds);

                using EventPipeSessionController gcDumpSession = new(processId, diagnosticPort, new List<EventPipeProvider> {
                    new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, (long)(ClrTraceEventParser.Keywords.GCHeapSnapshot))
                });
                log.WriteLine("{0,5:n1}s: gcdump EventPipe Session started", getElapsed().TotalSeconds);

                int gcNum = -1;

                gcDumpSession.Source.Clr.GCStart += delegate (GCStartTraceData data)
                {
                    if (gcDumpSession.UseWildcardProcessId)
                    {
                        processId = data.ProcessID;
                    }
                    if (data.ProcessID != processId)
                    {
                        return;
                    }

                    eventPipeDataPresent = true;

                    if (gcNum < 0 && data.Depth == 2 && data.Type != GCType.BackgroundGC)
                    {
                        gcNum = data.Count;
                        log.WriteLine("{0,5:n1}s: .NET Dump Started...", getElapsed().TotalSeconds);
                    }
                };

                gcDumpSession.Source.Clr.GCStop += delegate (GCEndTraceData data)
                {
                    if (data.ProcessID != processId)
                    {
                        return;
                    }

                    if (data.Count == gcNum)
                    {
                        log.WriteLine("{0,5:n1}s: .NET GC Complete.", getElapsed().TotalSeconds);
                        dumpComplete = true;
                    }
                };

                gcDumpSession.Source.Clr.GCBulkNode += delegate (GCBulkNodeTraceData data)
                {
                    if (data.ProcessID != processId)
                    {
                        return;
                    }

                    eventPipeDataPresent = true;

                    if ((getElapsed() - lastEventPipeUpdate).TotalMilliseconds > 500)
                    {
                        log.WriteLine("{0,5:n1}s: Making GC Heap Progress...", getElapsed().TotalSeconds);
                    }

                    lastEventPipeUpdate = getElapsed();
                };

                if (memoryGraph != null)
                {
                    dumper.SetupCallbacks(memoryGraph, gcDumpSession.Source, gcDumpSession.UseWildcardProcessId ? null : processId.ToString());
                }

                // Set up a separate thread that will listen for EventPipe events coming back telling us we succeeded.
                Task readerTask = Task.Run(() => {
                    // cancelled before we began
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    log.WriteLine("{0,5:n1}s: Starting to process events", getElapsed().TotalSeconds);
                    gcDumpSession.Source.Process();
                    log.WriteLine("{0,5:n1}s: EventPipe Listener dying", getElapsed().TotalSeconds);
                }, ct);

                for (; ; )
                {
                    if (ct.IsCancellationRequested)
                    {
                        log.WriteLine("{0,5:n1}s: Cancelling...", getElapsed().TotalSeconds);
                        break;
                    }

                    if (readerTask.Wait(100))
                    {
                        break;
                    }

                    if (!eventPipeDataPresent && getElapsed().TotalSeconds > 5)      // Assume it started within 5 seconds.
                    {
                        log.WriteLine("{0,5:n1}s: Assume no .NET Heap", getElapsed().TotalSeconds);
                        break;
                    }

                    if (getElapsed().TotalSeconds > timeout)       // Time out after `timeout` seconds. defaults to 30s.
                    {
                        log.WriteLine("{0,5:n1}s: Timed out after {1} seconds", getElapsed().TotalSeconds, timeout);
                        break;
                    }

                    if (dumpComplete)
                    {
                        break;
                    }
                }

                Task stopTask = Task.Run(() => {
                    log.WriteLine("{0,5:n1}s: Shutting down gcdump EventPipe session", getElapsed().TotalSeconds);
                    gcDumpSession.EndSession();
                    log.WriteLine("{0,5:n1}s: gcdump EventPipe session shut down", getElapsed().TotalSeconds);
                }, ct);

                try
                {
                    while (!Task.WaitAll(new Task[] { readerTask, stopTask }, 1000))
                    {
                        log.WriteLine("{0,5:n1}s: still reading...", getElapsed().TotalSeconds);
                    }
                }
                catch (AggregateException ae) // no need to throw if we're just cancelling the tasks
                {
                    foreach (Exception e in ae.Flatten().InnerExceptions)
                    {
                        if (e is not TaskCanceledException)
                        {
                            throw;
                        }
                    }
                }

                log.WriteLine("{0,5:n1}s: gcdump EventPipe Session closed", getElapsed().TotalSeconds);

                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                if (eventPipeDataPresent)
                {
                    dumper.ConvertHeapDataToGraph();        // Finish the conversion.
                }
            }
            catch (Exception e)
            {
                log.WriteLine($"{getElapsed().TotalSeconds,5:n1}s: [Error] Exception during gcdump: {e}");
            }

            log.WriteLine("[{0,5:n1}s: Done Dumping .NET heap success={1}]", getElapsed().TotalSeconds, dumpComplete);

            return dumpComplete;
        }
    }

    internal sealed class EventPipeSessionController : IDisposable
    {
        private List<EventPipeProvider> _providers;
        private DiagnosticsClient _client;
        private EventPipeSession _session;
        private EventPipeEventSource _source;
        private int _pid;
        private IpcEndpointConfig _diagnosticPort;

        public IReadOnlyList<EventPipeProvider> Providers => _providers.AsReadOnly();
        public EventPipeEventSource Source => _source;

        public bool UseWildcardProcessId => _diagnosticPort != null;

        public EventPipeSessionController(int pid, string diagnosticPort, List<EventPipeProvider> providers, bool requestRundown = true)
        {
            if (string.IsNullOrEmpty(diagnosticPort))
            {
                try
                {
                    string defaultAddress = PidIpcEndpoint.GetDefaultAddress(pid);
                    if (!string.IsNullOrEmpty(defaultAddress) && PidIpcEndpoint.IsDefaultAddressDSRouter(pid, defaultAddress))
                    {
                        diagnosticPort = defaultAddress + ",connect";
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(diagnosticPort))
            {
                _diagnosticPort = IpcEndpointConfig.Parse(diagnosticPort);
                if (!_diagnosticPort.IsConnectConfig)
                {
                    throw new ArgumentException("DiagnosticPort is only supporting connect mode.");
                }
            }

            _pid = pid;
            _providers = providers;

            if (_diagnosticPort != null)
            {
                _client = new DiagnosticsClient(_diagnosticPort);
            }
            else
            {
                _client = new DiagnosticsClient(pid);
            }

            _session = _client.StartEventPipeSession(providers, requestRundown, 1024);
            _source = new EventPipeEventSource(_session.EventStream);
        }

        public void EndSession()
        {
            _session.Stop();
        }

        #region IDisposable Support
        private bool disposedValue; // To detect redundant calls

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _session?.Dispose();
                    _source?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
