using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.Diagnostics.Tools.Trace.DiagnosticProfileHandlers
{
    /// <summary>
    /// This class handles parsing for GC-triggered Pauses - specifically by parsing the GC/SuspendEEStart, GC/SuspendEEStop, GC/GCStart, GC/GCEnd events.
    /// </summary>
    internal class GcPauseHandler : IDiagnosticProfileHandler
    {
        private DateTime lastKnownEESuspensionStartTimeStamp;
        private Dictionary<int, DateTime> GCStartTimeCache; // Cache of start-times for each GC to compute GC pause times.

        public GcPauseHandler()
        {
            GCStartTimeCache = new Dictionary<int, DateTime>();
        }

        /// <summary>
        /// Adds the GC Handler
        /// NOTE: This handler is not thread-safe. If we ever modify it to be used from multiple threads, we need to modify the read/writes to 
        /// the GCStartTimeCache dictionary to be thread-safe.
        /// </summary>
        /// <param name="source"></param>
        public void RegisterHandler(EventPipeEventSource source)
        {
            source.Clr.GCStart += (GCStartTraceData data) =>
            {
                GCStartTimeCache[data.Count] = data.TimeStamp;
                Console.WriteLine($"[CLR-GC|GCStart|{data.TimeStamp.ToString("s", CultureInfo.InvariantCulture)}] Count={data.Count};Reason={GetGCReason(data.Reason)};Type={GetGCType(data.Type)}");
            };

            source.Clr.GCStop += (GCEndTraceData data) =>
            {
                if (GCStartTimeCache.Remove(data.Count, out DateTime startTime))
                {
                    Console.WriteLine($"[CLR-GC|GCEnd|{data.TimeStamp.ToString("s", CultureInfo.InvariantCulture)}] Count={data.Count};Generation={data.Depth};PauseTime={(data.TimeStamp - startTime).TotalMilliseconds} ms");
                }
            };

            source.Clr.GCSuspendEEStart += (GCSuspendEETraceData data) =>
            {
                lastKnownEESuspensionStartTimeStamp = data.TimeStamp;
                Console.WriteLine($"[CLR-GC|EESuspendStart|{data.TimeStamp.ToString("s", CultureInfo.InvariantCulture)}] Reason={GetEESuspendReason(data.Reason)}");
            };

            source.Clr.GCSuspendEEStop += (GCNoUserDataTraceData data) =>
            {
                Console.WriteLine($"[CLR-GC|EESuspendStop|{data.TimeStamp.ToString("s", CultureInfo.InvariantCulture)}] SuspensionTime: {(data.TimeStamp - lastKnownEESuspensionStartTimeStamp).TotalMilliseconds} ms");
            };
        }

        private string GetGCReason(GCReason reason)
        {
            return reason switch
            {
                GCReason.AllocSmall => "AllocSmall",
                GCReason.Induced => "Induced",
                GCReason.LowMemory => "LowMemory",
                GCReason.Empty => "Empty",
                GCReason.AllocLarge => "AllocLarge",
                GCReason.OutOfSpaceSOH => "OutOfSpaceSOH",
                GCReason.OutOfSpaceLOH => "OutOfSpaceLOH",
                GCReason.InducedNotForced => "InducedNotForced",
                GCReason.Internal => "Internal",
                GCReason.InducedLowMemory => "InducedLowMemory",
                GCReason.InducedCompacting => "InducedCompacting",
                GCReason.LowMemoryHost => "LowMemoryHost",
                GCReason.PMFullGC => "PMFullGC",
                GCReason.LowMemoryHostBlocking => "LowMemoryHostBlocking",
                _ => "UNKNOWN"
            };
        }

        private string GetGCType(GCType type)
        {
            return type switch
            {
                GCType.BackgroundGC => "Background",
                GCType.ForegroundGC => "Foreground",
                GCType.NonConcurrentGC => "NonConcurrent",
                _ => "UNKNOWN"
            };
        }

        private string GetEESuspendReason(GCSuspendEEReason reason)
        {
            return reason switch
            {
                GCSuspendEEReason.SuspendForAppDomainShutdown => "AppDomainShutdown",
                GCSuspendEEReason.SuspendForCodePitching => "CodePitching",
                GCSuspendEEReason.SuspendForDebugger => "Debugger",
                GCSuspendEEReason.SuspendForDebuggerSweep => "DebuggerSweep",
                GCSuspendEEReason.SuspendForGC => "GC",
                GCSuspendEEReason.SuspendForGCPrep => "GCPrep",
                GCSuspendEEReason.SuspendForShutdown => "Shutdown",
                GCSuspendEEReason.SuspendOther => "Other",
                _ => "UNKNOWN"
            };
        }
    }
}
