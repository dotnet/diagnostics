# Diagnostics Client Library API Design 

## Intro
The Diagnostics Client Library (currently named as "Runtime Client Library") is a managed library that can be used to interact with the .NET runtime via the diagnostics IPC protocol as documented in https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md. It provides managed classes for invoking the diagnostics IPC commands programmatically, and can be extended to write various diagnostics tools. It also comes with various classes that should facilitate interacting with the diagnostics IPC commands.

The name "Diagnostics Client Library" comes from the fact that we call the runtime (CoreCLR) component responsible for accepting and handling the diagnostics IPC commands the "diagnostics server" - https://github.com/dotnet/coreclr/blob/master/src/vm/diagnosticserver.h. Since this library is a managed library on the other side of the IPC protocol responsible for communicating with the runtime's "diagnostics server", calling this the "Diagnostics Client Library" made sense.

## Goals

The goal of this library is as following:

* Serve as an implementation of the IPC protocol to communicate with CoreCLR's diagnostics server.
* Provide an easy-to-use API for any library/tools authors to utilize the IPC protocol 

## Non-Goals

* Provide tool-specific functionalities that are too high-level (i.e. dumping the GC heap, parsing counter payload, etc.) This will broaden the scope of this library too far and will cause complexity 
* Parse event payloads (i.e. - This is also command-specific and can be done by other libraries.

## Sample Code:

#### 1. Attaching to a process and dumping out all the event name in real time to the console
```cs
using Microsoft.Diagnostics.NETCore.Client;

public void PrintEvents(int processId, IEnumerable<EventPipeProvider> providers, bool requestRundown, int circularBufferSizeMB)
{
    EventPipeSession session = DiagnosticsClient.StartTracing(processId, providers, requestRundown, circularBufferSizeMB);
    EventPipeEventSource source = new EventPipeEventSource(session.stream);

    source.Dynamic.All += (TraceEvent obj) => {
        Console.WriteLine(obj.EventName);
    }
    try
    {
        source.Process();
        ShouldExit.WaitOne();
    }
    catch (Exception e)
    {
    }
    finally
    {
        DiagnosticsClient.StopTracing(session);
    }
}
```

#### 2. Trigger a core dump when CPU usage goes above a certain threshold
```cs

using Microsoft.Diagnostics.NETCore.Client;

public void TriggerDumpOnCpuUsage(int processId, int threshold)
{
    EventPipeSession session = DiagnosticsClient.StartTracing(processId, providers);
    EventPipeEventSource source = new EventPipeEventSource(session.stream)
    source.Dynamic.All += (TraceEvent obj) => {
        if (obj.EventName.Equals("EventCounters"))
        {
            // I know this part is ugly. But this is all TraceEvent.
            IDictionary<string, object> payloadFields = (IDictionary<string, object>)(obj.GetPayloadValueByName("Payload"));
            if (payloadFields["Name"].ToString().Equals("cpu-usage"))
            {
                double cpuUsage = Double.Parse(payloadFields["Mean"]);
                if (cpuUsage > (double)threshold)
                {
                    DiagnosticsClient.CollectCoreDump(processId);
                }
            }
        }
    }
    try
    {
        source.Process();
        shouldExit.WaitOne();
    }
    // TraceEvent throws a generic Exception when the target process exists first. 
    // This also needs some fix on TraceEvent side.
    catch (Exception e) { } 
    finally
    {
        DiagnosticsClient.StopTracing(session);
    }
}
```

#### 3. Trigger a CPU trace when CPU usage goes above a certain threshold
```cs

using Microsoft.Diagnostics.NETCore.Client;
using System.Diagnostics;
using System.IO;
using System.Threading.Task;

public void TriggerTraceOnCpuUsage(int processId, int threshold, int traceDuration, string traceName)
{
    IEnumerable<EventPipeProvider> runtimeCounterProvider = new List<EventPipeProvider>() { 
        new EventPipeProvider("System.Runtime", 1, 1, "EventCounterIntervalSec=1")
    };

    IEnumerable<EventPipeProvider> cpuProvider = new List<EventPipeProvider>() { 
        new EventPipeProvider("Microsoft-Windows-DotNETRuntime", ClrTraceEventParser.Keywords.Default, EventLevel.Informational)
    };

    EventPipeSession counterSession = DiagnosticsClient.StartTracing(processId, runtimeCounterProvider);
    EventPipeEventSource source = new EventPipeEventSource(counterSession.stream)
    source.Dynamic.All += (TraceEvent obj) => {
        if (obj.EventName.Equals("EventCounters"))
        {
            // I know this part is ugly. But this is all TraceEvent.
            IDictionary<string, object> payloadFields = (IDictionary<string, object>)(obj.GetPayloadValueByName("Payload"));
            if (payloadFields["Name"].ToString().Equals("cpu-usage"))
            {
                double cpuUsage = Double.Parse(payloadFields["Mean"]);
                if (cpuUsage > (double)threshold)
                {
                    EventPipeSession traceSession = DiagnosticsClient.StartTracing(processId, cpuProvider);
                    if (traceSesssion != null)
                    {
                        await Task.Run(() => {
                            var buffer = new byte[16 * 1024];
                            var fs = new FileStream(traceName, FileMode.Create, FileAccess.Write))
                            Stopwatch sw = new Stopwatch();
                            sw.Start();
                            while (sw.Elapsed.Seconds < traceDuration)
                            {
                                int nBytesRead = traceSession.Stream.Read(buffer, 0, buffer.Length);
                                if (nBytesRead <= 0)
                                    break;
                                fs.Write(buffer, 0, nBytesRead);
                            }
                            DiagnosticsClient.StopTracing(traceSession);
                            shouldExit.Set();
                        });
                    }
                }
            }
        }
    }
    try
    {
        source.Process();
        shouldExit.WaitOne();
    }
    catch (Exception e) { } 
    finally
    {
        DiagnosticsClient.StopTracing(counterSession);
    }
}
```

## API Descriptions

At a high level, the DiagnosticsClient class provides static methods that the user may call to invoke diagnostics IPC commands (i.e. start an EventPipe session, request a core dump, etc.) The library also provides several classes that may be helpful for invoking these commands. These commands are described in more detail in the diagnostics IPC protocol documentation available here: https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md#commands. 

### ProcessLocator
This is a helper class that finds processes to attach to.
```cs
namespace Microsoft.Diagnostics.NETCore.Client
{
    /// <summary>
    /// A utility class that contain various helper methods to interact with the diagnostics IPC.
    /// </summary>
    public class ProcessLocator
    {
        /// <summary>
        /// Get all the active processes that can be attached to.
        /// </summary>
        /// <returns>
        /// IEnumerable of all the active process IDs.
        /// </returns>
        public static IEnumerable<int> GetPublishedProcesses();
    }
}
```


### DumpType (enum)
This is an enum for the dump type

```cs
namespace Microsoft.Diagnostics.NETCore.Client
{
    public enum DumpType
    {
        Normal = 1,
        WithHeap = 2,
        Triage = 3,
        Full = 4
    }
}
```


### DiagnosticsClient
This is a top-level class with static methods to send various diagnostics command to the runtime.
```cs
namespace Microsoft.Diagnostics.NETCore.Client
{
    public class DiagnosticsClient
    {
        /// <summary>
        /// Start tracing the application via CollectTracing2 command.
        /// </summary>
        /// <param name="processId">Target process' ID</param>
        /// <param name="providers">An IEnumerable containing the list of Providers to turn on.</param>
        /// <param name="circularBufferMB">The size of the runtime's buffer for collecting events in MB</param>
        /// <param name="requestRundown">If true, request rundown events from the runtime</param>
        /// <returns>
        /// An EventPipeSession object representing the EventPipe session that just started.
        /// </returns> 
        public static EventPipeSession StartTracing(int processId, IEnumerable<EventPipeProvider> providers, int circularBufferMB=256, bool requestRundown=true)

        /// <summary>
        /// Stop the EventPipe session provided as an argument.
        /// </summary>
        /// <param name="session">The EventPipeSession to be stopped.
        /// <returns>
        /// true if stopping the tracing session succeeded. false otherwise. 
        /// </returns> 
        public static bool StopTracing(EventPipeSession session)

        /// <summary>
        /// Trigger a core dump generation.
        /// </summary> 
        /// <param name="processId">Target process' ID</param>
        /// <param name="dumpName">Name of the dump to be generated</param>
        /// <param name="dumpType">Type of the dump to be generated</param>
        /// <param name="logDumpGeneration">When set to true, display the dump generation debug log to the console</param>
        /// <returns>
        /// true if stopping the tracing session succeeded. false otherwise. 
        /// </returns> 
        public static GenerateCoreDump(int processId, string dumpName, DumpType dumpType, bool logDumpGeneration)

        /// <summary>
        /// Attach a profiler.
        /// </summary>
        /// <param name="processId">Target process' ID</param>
        /// <param name="attachTimeout">Timeout in seconds</param>
        /// <param name="attachTimeout">
        public static int AttachProfiler(int processId, uint attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData);

    }
}
```


### Exceptions that can be thrown 

```cs
namespace Microsoft.Diagnostics.NETCore.Client
{
    // Generic wrapper for exceptions thrown by this library
    public class DiagnosticsClientExcpetion : Exception {} 

    // When a certian command is not supported by either the library or the target process' runtime
    public class UnsupportedProtocolException : DiagnosticsClientException {} 

    // When the runtime is no longer availble for attaching.
    public class ServerNotAvailableException : DiagnosticsClientException {} 

    // When the runtime responded with an error
    public class ServerThrownException : DiagnosticsClientException {} 
}
```

## Micorosft.Diagnostics.Client.EventPipe namespace

### EventPipeProvider
A class that describes an EventPipe provider.
```cs
namespace Microsoft.Diagnostics.Client
{
    public class EventPipeProvider
    {
        public EventPipeProvider(
            string name,
            long keywords = ulong.MaxValue,
            EventLevel eventLevel = EventLevel.Verbose,
            string filterData = null)

        public long Keywords { get; }

        public EventLevel EventLevel { get; }

        public string Name { get; }

        public IEnumerable<KeyValuePair<string, string>> FilterData { get; }

        public override string ToString();
        
        public override bool Equals(object obj);

        public override int GetHashCode();

        public static bool operator ==(Provider left, Provider right);

        public static bool operator !=(Provider left, Provider right);
    }
}
```

### EventPipeSession
This is a class to represent an EventPipeSession. It is meant to be immutable and acts as a handle to each session that has been started. 

```cs
namespace Microsoft.Diagnostics.Client
{
    public class EventPipeSession
    {
        public EventPipeSession(
            int processId,
            long sessionId,
            IEnumerable<EventPipeProvider> providers,
            int circularBufferMB,
            bool rundownRequested,
            Stream stream
        )
        public int ProcessId { get; }
        public int SessionId { get; } 
        public ReadOnlyList<EventPipeProvider> Providers { get ; }
        public int CircularBufferMB { get; }
        public bool RundownRequested { get; }
        public Stream Stream { get; };
    }
}
```