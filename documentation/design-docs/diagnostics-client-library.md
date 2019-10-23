# Diagnostics Client Library API Design 

## Intro
The Diagnostics Client Library (currently named as "Runtime Client Library") is a managed library that can be used to interact with the .NET runtime via the diagnostics IPC protocol as documented in https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md.

The name "Diagnostics Client Library" comes from the fact that we call the runtime (CoreCLR) component responsible for accepting and handling the diagnostics IPC commands the "diagnostics server" - https://github.com/dotnet/coreclr/blob/master/src/vm/diagnosticserver.h. Since this library is a managed library on the other side of the IPC protocol responsible for communicating with the runtime's "diagnostics server", calling this the "Diagnostics Client Library" made sense.

## Goals

The goal of this library is as following:

* Serve as an implementation of the IPC protocol to communicate with CoreCLR's diagnostics server.
* Provide an easy-to-use API for any library/tools authors to utilize the IPC protocol 

## Non-Goals

* Provide tool-specific functionalities that are too high-level (i.e. dumping the GC heap, parsing counter payload, etc.) This will broaden the scope of this library too far and will cause complexity 
* Parse event payloads (i.e. - This is also command-specific and can be done by other libraries.

## Sample Code:

### 1. Attaching to a process and dumping out all the event name in real time to the console
```cs

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
        DiagnosticsClient.StopTracing(session); // Cleanup
    }
}
```

#### 2. Trigger dump if CPU usage goes above a certain threshold
```cs
public void TriggerDumpOnCPUUsage(int usage, int processId, int threshold)
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
    catch (Exception e) { } // TraceEvent throws a generic Exception when the target process exists first. This also needs some fix on TraceEvent side.
    finally
    {
        DiagnosticsClient.StopTracing(session);
    }
}
```


## API Descriptions

At a high level, the DiagnosticsClient library provides a `CommandHandler` class for each of the command specified as part of the diagnostics IPC protocol described here: https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md#commands. For example, `EventPipeCommandHandler` handles all the `EventPipe` commands that are specified, such as `CollectTracing` and `CollectTracing2`.  For each of these commands, there should be a method that handles it. 

Currently the handler classes are scattered across in different format. On top of that they are all static methods. To make it cleaner and allow a single instance of the CommandHandler class to be responsible for handling communication with a *single process*, I propose that these CommandHandler methods become instance methods and their constructor to take in the process ID of the process they are responsible for communicating with.

There are also helper methods that contain various util methods for talking to the IPC channel. These are included in the `DiagnosticsIpcHelper` class.

We may create additional namespaces under Microsoft.Diagnostics.Client for command-specific classes that may be useful. i.e. `Microsoft.Diagnostics.Client.EventPipe`.

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
        /// <returns>
        /// An EventPipeSession object representing the EventPipe session that just started.
        /// </returns> 
        public static EventPipeSession StartTracing(int processId, IEnumerable<EventPipeProvider> providers, int circularBufferMB=256, bool requestRundown=true)

        /// <summary>
        /// Stop the EventPipe session provided as an argument.
        /// </summary> 
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
    }
}
```

### DumpCommandHandler
This is a CommandHandler class for sending Dump commands across the diagnostics IPC channel.
```cs
namespace Microsoft.Diagnostics.Client
{
    public class DumpCommandHandler
    {
        public DumpCommandHandler(int processId);
        public int GenerateCoreDump();
    }
}
```


### ProfilerCommandHandler
This is a CommandHandler class for sending Profiler commands across the diagnostics IPC channel.
```cs
namespace Microsoft.Diagnostics.Client
{
    public class ProfilerCommandHandler
    {
        public ProfilerCommandHandler(int processId);
        public int AttachProfiler(uint attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData);
    }
}
```


### Exceptions that can be thrown 
```cs
namespace Microsoft.Diagnostics.Client.Exceptions
{
    public class UnknownCommandException : Exception {}
    public class UnknownMagicException : Exception {}
    public class EventPipeInvalidArgumentException : Exception {}
    public class DumpInvalidArgumentException : Exception {}
    public class ProfilerInvalidArgumentException : Exception {}
}
```

## Micorosft.Diagnostics.Client.EventPipe namespace

### EventPipeCommandSet
An enum for all EventPipe commands.
``` cs
namespace Micorosft.Diagnostics.Client.EventPipe
{
    public enum EventPipeCommandSet
    {
        StopTracing,
        CollectTracing,
        CollectTracing2
    }
}
```

### EventPipeSerializationFormat
An enum for all EventPipe serialization format.
```cs
namespace Microsoft.Diagnostics.Client.EventPipe
{
    public enum EventPipeSerializationFormat
    {
        NetPerf,
        NetTrace
    }
}
```

### EventPipeProvider
A class that describes an EventPipe provider.
```cs
namespace Microsoft.Diagnostics.Client.EventPipe
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

        public string ToDisplayString();

        public override string ToString();
        
        public override bool Equals(object obj);

        public override int GetHashCode();

        public static bool operator ==(Provider left, Provider right);

        public static bool operator !=(Provider left, Provider right);
    }
}
```

#### 3. Maintain multiple tracing sessions to the same process for different purposes
```cs
public void GetGCTrace()
{
    Stream pipe = EventPipeClient.Start(processId, )
}

public void GetCounterTrace()
{

}

public static void Main()
{

}
```