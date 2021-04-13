# Diagnostics Client Library API Design 

## Intro
The Diagnostics Client Library (currently named as "Runtime Client Library") - `Microsoft.Diagnostics.NetCore.Client.dll` - is a managed library that can be used to interact with the .NET runtime via the diagnostics IPC protocol as documented in https://github.com/dotnet/diagnostics/blob/main/documentation/design-docs/ipc-protocol.md. It provides managed classes for invoking the diagnostics IPC commands programmatically, and can be extended to write various diagnostics tools. It also comes with various classes that should facilitate interacting with the diagnostics IPC commands.

The name "Diagnostics Client Library" comes from the fact that we call the runtime (CoreCLR) component responsible for accepting and handling the diagnostics IPC commands the "diagnostics server" - https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/diagnosticserver.h. Since this library is a managed library on the other side of the IPC protocol responsible for communicating with the runtime's "diagnostics server", calling this the "Diagnostics Client Library" made sense.

## Goals

The goal of this library is as following:

* Serve as an implementation of the IPC protocol to communicate with CoreCLR's diagnostics server.
* Provide an easy-to-use API for any library/tools authors to utilize the IPC protocol 

## Non-Goals

* Provide tool-specific functionalities that are too high-level (i.e. dumping the GC heap, parsing counter payload, etc.) This will broaden the scope of this library too far and will cause complexity 
* Parse event payloads (i.e. - This is also command-specific and can be done by other libraries.

## Sample Code:

Here are some sample code showing the usage of this library.

#### 1. Attaching to a process and dumping out all the runtime GC events in real time to the console
This sample shows an example where we trigger an EventPipe session with the .NET runtime provider with the GC keyword at informational level, and use `EventPipeEventSource` (provided by the TraceEvent library) to parse the events coming in and print the name of each event to the console in real time.

```cs
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;

public void PrintRuntimeGCEvents(int processId)
{
    var providers = new List<EventPipeProvider>()
    {
        new EventPipeProvider("Microsoft-Windows-DotNETRuntime",
            EventLevel.Informational, (long)ClrTraceEventParser.Keywords.GC)
    };

    var client = new DiagnosticsClient(processId);
    using (var session = client.StartEventPipeSession(providers, false))
    {
        var source = new EventPipeEventSource(session.EventStream);

        source.Clr.All += (TraceEvent obj) => {
            Console.WriteLine(obj.EventName);
        }
        try
        {
            source.Process();
        }
        // NOTE: This exception does not currently exist. It is something that needs to be added to TraceEvent.
        catch (EventStreamException e)
        {
            Console.WriteLine("Error encountered while processing events");
            Console.WriteLine(e.ToString());
        }
    }
}
```

#### 2. Write a core dump. 
This sample shows how to trigger a dump using `DiagnosticsClient`.
```cs
using Microsoft.Diagnostics.NetCore.Client;

public void TriggerCoreDump(int processId)
{
    var client = new DiagnosticsClient(processId);
    client.WriteDump(DumpType.Normal);
}
```

#### 3. Trigger a core dump when CPU usage goes above a certain threshold
This sample shows an example where we monitor the `cpu-usage` counter published by the .NET runtime and use the `WriteDump` API to write out a dump when the CPU usage grows beyond a certain threshold.
```cs

using Microsoft.Diagnostics.NETCore.Client;

public void TriggerDumpOnCpuUsage(int processId, int threshold)
{
    var providers = new List<EventPipeProvider>()
    {
        new EventPipeProvider(
            "System.Runtime",
            EventLevel.Informational,
            (long)ClrTraceEventParser.Keywords.None,
            new Dictionary<string, string>() {
                { "EventCounterIntervalSec", "1" }
            }
        )
    };
    var client = new DiagnosticsClient(processId);
    using(var session = client.StartEventPipeSession(providers))
    {
        var source = new EventPipeEventSource(session.EventStream);
        source.Dynamic.All += (TraceEvent obj) =>
        {
            if (obj.EventName.Equals("EventCounters"))
            {
                // I know this part is ugly. But this is all TraceEvent.
                IDictionary<string, object> payloadVal = (IDictionary<string, object>)(obj.PayloadValue(0));
                IDictionary<string, object> payloadFields = (IDictionary<string, object>)(payloadVal["Payload"]);
                if (payloadFields["Name"].ToString().Equals("cpu-usage"))
                {
                    double cpuUsage = Double.Parse(payloadFields["Mean"]);
                    if (cpuUsage > (double)threshold)
                    {
                        client.WriteDump(DumpType.Normal, "/tmp/minidump.dmp");
                    }
                }
            }
        }
        try
        {
            source.Process();
        }
        catch (EventStreamException) {}

        }
    }
}
```

#### 4. Trigger a CPU trace for given number of seconds
This sample shows an example where we trigger an EventPipe session for certain period of time, with the default CLR trace keyword as well as the sample profiler, and read from the stream that gets created as a result and write the bytes out to a file. Essentially this is what `dotnet-trace` uses internally to write a trace file.

```cs

using Microsoft.Diagnostics.NETCore.Client;
using System.Diagnostics;
using System.IO;
using System.Threading.Task;

public void TraceProcessForDuration(int processId, int duration, string traceName)
{
    var cpuProviders = new List<EventPipeProvider>()
    {
        new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Default),
        new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.None)
    };
    var client = new DiagnosticsClient(processId);
    using (var traceSession = client.StartEventPipeSession(cpuProviders))
    {
        Task copyTask = Task.Run(async () =>
        {
            using (FileStream fs = new FileStream(traceName, FileMode.Create, FileAccess.Write))
            {
                await traceSession.EventStream.CopyToAsync(fs);
            }
        });

        copyTask.Wait(duration * 1000);
        traceSession.Stop();
    }
}
```

#### 5. Print names of all .NET processes that published a diagnostics server to connect

This sample shows how to use `DiagnosticsClient.GetPublishedProcesses` API to print the names of the .NET processes that published a diagnostics IPC channel. 

```cs
using Microsoft.Diagnostics.NETCore.Client;
using System.Linq;

public static void PrintProcessStatus()
{
    var processes = DiagnosticsClient.GetPublishedProcesses()
        .Select(GetProcessById)
        .Where(process => process != null)

    foreach (var process in processes)
    {
        Console.WriteLine($"{process.ProcessName}");
    }
}
```


#### 6. Live-parsing events for a specified period of time. 

This sample shows an example where we create two tasks, one that parses the events coming in live with `EventPipeEventSource` and one that reads the console input for a user input signaling the program to end. If the target app exists before the users presses enter, the app exists gracefully. Otherwise, `inputTask` will send the Stop command to the pipe and exit gracefully.

```cs
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;

public static void PrintEventsLive(int processId)
{
    var providers = new List<EventPipeProvider>()
    {
        new EventPipeProvider("Microsoft-Windows-DotNETRuntime",
            EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Default)
    };
    var client = new DiagnosticsClient(processId);
    using (var session = client.StartEventPipeSession(providers, false))
    {

        Task streamTask = Task.Run(() =>
        {
            var source = new EventPipeEventSource(session.EventStream);
            source.Dynamic.All += (TraceEvent obj) =>
            {
                Console.WriteLine(obj.EventName);
            };
            try
            {
                source.Process();
            }
            // NOTE: This exception does not currently exist. It is something that needs to be added to TraceEvent.
            catch (Exception e)
            {
                Console.WriteLine("Error encountered while processing events");
                Console.WriteLine(e.ToString());
            }
        });

        Task inputTask = Task.Run(() =>
        {
            Console.WriteLine("Press Enter to exit");
            while (Console.ReadKey().Key != ConsoleKey.Enter)
            { 
                Thread.Sleep(100);
            }
            session.Stop();
        });

        Task.WaitAny(streamTask, sleepTask);
    }
}
```

#### 7. Attach a ICorProfiler profiler

This sample shows how to attach an ICorProfiler to a process (profiler attach).
```cs
public static int AttachProfiler(int processId, Guid profilerGuid, string profilerPath)
{
    var client = new DiagnosticsClient(processId);
    return client.AttachProfiler(TimeSpan.FromSeconds(10), profilerGuid, profilerPath);
}
```

## API Descriptions

At a high level, the DiagnosticsClient class provides static methods that the user may call to invoke diagnostics IPC commands (i.e. start an EventPipe session, request a core dump, etc.) The library also provides several classes that may be helpful for invoking these commands. These commands are described in more detail in the diagnostics IPC protocol documentation available here: https://github.com/dotnet/diagnostics/blob/main/documentation/design-docs/ipc-protocol.md#commands. 


### DiagnosticsClient
This is a top-level class that contains methods to send various diagnostics command to the runtime.
```cs
namespace Microsoft.Diagnostics.NETCore.Client
{
    public class DiagnosticsClient
    {
        public DiagnosticsClient(int processId)

        /// <summary>
        /// Start tracing the application via CollectTracing2 command.
        /// </summary>
        /// <param name="providers">An IEnumerable containing the list of Providers to turn on.</param>
        /// <param name="requestRundown">If true, request rundown events from the runtime</param>
        /// <param name="circularBufferMB">The size of the runtime's buffer for collecting events in MB</param>
        /// <returns>
        /// An EventPipeSession object representing the EventPipe session that just started.
        /// </returns> 
        public EventPipeSession StartEventPipeSession(IEnumerable<EventPipeProvider> providers, bool requestRundown=true, int circularBufferMB=256)

        /// <summary>
        /// Trigger a core dump generation.
        /// </summary> 
        /// <param name="dumpType">Type of the dump to be generated</param>
        /// <param name="dumpPath">Full path to the dump to be generated. By default it is /tmp/coredump.{pid}</param>
        /// <param name="logDumpGeneration">When set to true, display the dump generation debug log to the console.</param>
        public void WriteDump(DumpType dumpType, string dumpPath=null, bool logDumpGeneration=false)

        /// <summary>
        /// Attach a profiler.
        /// </summary>
        /// <param name="attachTimeout">Timeout for attaching the profiler</param>
        /// <param name="profilerGuid">Guid for the profiler to be attached</param>
        /// <param name="profilerPath">Path to the profiler to be attached</param>
        /// <param name="additionalData">Additional data to be passed to the profiler</param>
        public void AttachProfiler(TimeSpan attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData=null);

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


### Exceptions that can be thrown 

```cs
namespace Microsoft.Diagnostics.NETCore.Client
{
    // Generic wrapper for exceptions thrown by this library
    public class DiagnosticsClientException : Exception {}

    // When a certian command is not supported by either the library or the target process' runtime
    public class UnsupportedProtocolException : DiagnosticsClientException {}

    // When the runtime is no longer availble for attaching.
    public class ServerNotAvailableException : DiagnosticsClientException {}

    // When the runtime responded with an error
    public class ServerErrorException : DiagnosticsClientException {}
}
```

### EventPipeProvider
A class that describes an EventPipe provider.
```cs
namespace Microsoft.Diagnostics.Client
{
    public class EventPipeProvider
    {
        public EventPipeProvider(
            string name,
            EventLevel eventLevel,
            long keywords = 0,
            IDictionary<string, string> arguments = null)

        public long Keywords { get; }

        public EventLevel EventLevel { get; }

        public string Name { get; }

        public IDictionary<string, string> Arguments { get; }

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
    public class EventPipeSession : IDisposable
    {
        public Stream EventStream { get; };

        ///<summary>
        /// Stops the given session
        ///</summary>
        public void Stop();
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
