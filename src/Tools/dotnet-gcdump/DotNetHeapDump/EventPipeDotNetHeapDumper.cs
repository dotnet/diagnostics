// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Graphs;
using Microsoft.Diagnostics.Tools.RuntimeClient;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.GCDump
{
    public static class EventPipeDotNetHeapDumper
    {
        /// <summary>
        /// Given a factory for creating an EventPipe session with the appropriate provider and keywords turned on,
        /// generate a GCHeapDump using the resulting events.  The correct keywords and provider name
        /// are given as input to the Func eventPipeEventSourceFactory.
        /// </summary>
        /// <param name="processID"></param>
        /// <param name="eventPipeEventSourceFactory">A delegate for creating and stopping EventPipe sessions</param>
        /// <param name="memoryGraph"></param>
        /// <param name="log"></param>
        /// <param name="dotNetInfo"></param>
        /// <returns></returns>
        public static bool DumpFromEventPipe(CancellationToken ct, int processID, MemoryGraph memoryGraph, TextWriter log, DotNetHeapInfo dotNetInfo = null)
        {
            var sw = Stopwatch.StartNew();
            var dumper = new DotNetHeapDumpGraphReader(log)
            {
                DotNetHeapInfo = dotNetInfo
            };
            bool dumpComplete = false;
            bool listening = false;

            EventPipeSession gcDumpSession = null;
            Task readerTask = null;
            try
            {
                bool eventPipeDataPresent = false;
                TimeSpan lastEventPipeUpdate = sw.Elapsed;
                EventPipeSession typeFlushSession = null;
                bool fDone = false;
                var otherListening = false;
                log.WriteLine("{0,5:n1}s: Creating type table flushing task", sw.Elapsed.TotalSeconds);
                var typeTableFlushTask = Task.Factory.StartNew(() =>
                {
                    typeFlushSession = new EventPipeSession(processID, new List<Provider> { new Provider("Microsoft-DotNETCore-SampleProfiler") }, false);
                    otherListening = true;
                    log.WriteLine("{0,5:n1}s: Flushing the type table", sw.Elapsed.TotalSeconds);
                    typeFlushSession.Source.AllEvents += Task.Run(() => 
                    {
                        if (!fDone)
                        {
                            fDone = true;
                            typeFlushSession.EndSession();
                        }
                    });
                    typeFlushSession.Source.Process();
                    log.WriteLine("{0,5:n1}s: Done flushing the type table", sw.Elapsed.TotalSeconds);
                });

                await typeTableFlushTask;

                // Set up a separate thread that will listen for EventPipe events coming back telling us we succeeded. 
                readerTask = Task.Factory.StartNew(delegate
                {
                    // Start the providers and trigger the GCs.  
                    log.WriteLine("{0,5:n1}s: Requesting a .NET Heap Dump", sw.Elapsed.TotalSeconds);

                    gcDumpSession = new EventPipeSession(processID, new List<Provider> { new Provider("Microsoft-Windows-DotNETRuntime", (ulong)(ClrTraceEventParser.Keywords.GCHeapSnapshot)) });
                    int gcNum = -1;

                    gcDumpSession.Source.Clr.GCStart += delegate (GCStartTraceData data)
                    {
                        if (data.ProcessID != processID)
                        {
                            return;
                        }

                        eventPipeDataPresent = true;

                        if (gcNum < 0 && data.Depth == 2 && data.Type != GCType.BackgroundGC)
                        {
                            gcNum = data.Count;
                            log.WriteLine("{0,5:n1}s: .NET Dump Started...", sw.Elapsed.TotalSeconds);
                        }
                    };

                    gcDumpSession.Source.Clr.GCStop += delegate (GCEndTraceData data)
                    {
                        if (data.ProcessID != processID)
                        {
                            return;
                        }

                        if (data.Count == gcNum)
                        {
                            log.WriteLine("{0,5:n1}s: .NET GC Complete.", sw.Elapsed.TotalSeconds);
                            dumpComplete = true;
                        }
                    };

                    gcDumpSession.Source.Clr.GCBulkNode += delegate (GCBulkNodeTraceData data)
                    {
                        if (data.ProcessID != processID)
                        {
                            return;
                        }

                        eventPipeDataPresent = true;

                        if ((sw.Elapsed - lastEventPipeUpdate).TotalMilliseconds > 500)
                        {
                            log.WriteLine("{0,5:n1}s: Making GC Heap Progress...", sw.Elapsed.TotalSeconds);
                        }

                        lastEventPipeUpdate = sw.Elapsed;
                    };

                    if (memoryGraph != null)
                    {
                        dumper.SetupCallbacks(memoryGraph, gcDumpSession.Source, processID.ToString());
                    }

                    listening = true;
                    gcDumpSession.Source.Process();
                    log.WriteLine("{0,5:n1}s: EventPipe Listener dying", sw.Elapsed.TotalSeconds);
                });

                // Wait for thread above to start listening (should be very fast)
                while (!listening)
                {
                    readerTask.Wait(1);
                }

                for (; ; )
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    if (readerTask.Wait(100))
                    {
                        break;
                    }

                    if (!eventPipeDataPresent && sw.Elapsed.TotalSeconds > 5)      // Assume it started within 5 seconds.  
                    {
                        log.WriteLine("{0,5:n1}s: Assume no .NET Heap", sw.Elapsed.TotalSeconds);
                        break;
                    }

                    if (sw.Elapsed.TotalSeconds > 30)       // Time out after 30 seconds. 
                    {
                        log.WriteLine("{0,5:n1}s: Timed out after 20 seconds", sw.Elapsed.TotalSeconds);
                        break;
                    }

                    if (dumpComplete)
                    {
                        break;
                    }
                }

                log.WriteLine("{0,5:n1}s: Shutting down EventPipe session", sw.Elapsed.TotalSeconds);
                gcDumpSession.EndSession();

                while (!readerTask.Wait(1000))
                    log.WriteLine("{0,5:n1}s: still reading...", sw.Elapsed.TotalSeconds);

                if (eventPipeDataPresent)
                {
                    dumper.ConvertHeapDataToGraph();        // Finish the conversion.  
                }
            }
            catch (Exception e)
            {
                log.WriteLine($"{sw.Elapsed.TotalSeconds:0,5:n1}s: [Error] Exception during gcdump: {e.ToString()}");
            }

            log.WriteLine("[{0,5:n1}s: Done Dumping .NET heap success={1}]", sw.Elapsed.TotalSeconds, dumpComplete);

            return dumpComplete;
        }
    }

    internal class EventPipeSession
    {
        private List<Provider> _providers;
        private Stream _eventPipeStream;
        private EventPipeEventSource _source;
        private ulong _sessionId;
        private int _pid;

        public ulong SessionId => _sessionId;
        public IReadOnlyList<Provider> Providers => _providers.AsReadOnly();
        public EventPipeEventSource Source => _source;

        public EventPipeSession(int pid, List<Provider> providers, bool requestRundown = true)
        {
            _pid = pid;
            _providers = providers;
            var config = new SessionConfigurationV2(
                circularBufferSizeMB: 1024,
                format: EventPipeSerializationFormat.NetTrace,
                requestRundown: requestRundown,
                providers
            );
            _eventPipeStream = EventPipeClient.CollectTracing2(pid, config, out _sessionId);
            _source = new EventPipeEventSource(_eventPipeStream);
        }

        public void EndSession()
        {
            EventPipeClient.StopTracing(_pid, _sessionId);
        }
    }
}
