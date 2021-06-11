# Microsoft.Diagnostics.NETCore.Client API Documentation 

## Intro
Microsoft.Diagnostics.NETCore.Client (also known as the Diagnostics Client library) is a managed library that lets you interact with .NET Core runtime (CoreCLR) for various diagnostics related tasks, such as tracing, requesting a dump, or attaching an ICorProfiler. Using this library, you can write your own diagnostics tools customized for your particular scenario.

## Installing 
Microsoft.Diagnostics.NETCore.Client is available on [NuGet](https://www.nuget.org/packages/Microsoft.Diagnostics.NETCore.Client/). 


## Sample Code:

Here are some sample code showing the usage of this library.

#### 1. Attaching to a process and dumping out all the runtime GC events in real time to the console
This sample shows an example where we trigger an EventPipe session with the .NET runtime provider with the GC keyword at informational level, and use `EventPipeEventSource` (provided by the [TraceEvent library](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/)) to parse the events coming in and print the name of each event to the console in real time.

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
        };

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
using Microsoft.Diagnostics.NETCore.Client;

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
                var payloadFields = (IDictionary<string, object>)(obj.GetPayloadValueByName("Payload"));
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

        Task.WaitAny(streamTask, inputTask);
    }
}
```

#### 7. Attach a ICorProfiler profiler

This sample shows how to attach an ICorProfiler to a process (profiler attach).
```cs
public static void AttachProfiler(int processId, Guid profilerGuid, string profilerPath)
{
    var client = new DiagnosticsClient(processId);
    client.AttachProfiler(TimeSpan.FromSeconds(10), profilerGuid, profilerPath);
}
```

#### 8. Set an ICorProfiler to be used as the startup profiler

This sample shows how to request that the runtime use an ICorProfiler as the startup profiler (not as an attaching profiler). It is only valid to issue this command while the runtime is paused in "reverse server" mode.

```cs
public static void SetStartupProfilerProfiler(Guid profilerGuid, string profilerPath)
{
    var client = new DiagnosticsClient(processId);
    client.SetStartupProfiler(profilerGuid, profilerPath);
}
```

#### 9. Resume the runtime when it is paused in reverse server mode

This sample shows how a client can instruct the runtime to resume loading after it has been paused in "reverse server" mode.

```cs
public static void ResumeRuntime(Guid profilerGuid, string profilerPath)
{
    var client = new DiagnosticsClient(processId);
    client.ResumeRuntime();
}
```

## API Description

This section describes the APIs of the library.

#### class DiagnosticsClient
```cs
public DiagnosticsClient
{
    public DiagnosticsClient(int processId);
    public EventPipeSession StartEventPipeSession(IEnumerable<EventPipeProvider> providers, bool requestRundown=true, int circularBufferMB=256);
    public void WriteDump(DumpType dumpType, string dumpPath=null, bool logDumpGeneration=false);
    public void AttachProfiler(TimeSpan attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData=null);
    public static IEnumerable<int> GetPublishedProcesses();        
}
```



#### Methods

```csharp
public DiagnosticsClient(int processId);
```

Creates a new instance of `DiagnosticsClient` for a compatible .NET process running with process ID of `processId`.

`processID` : Process ID of the target application.



```csharp
public EventPipeSession StartEventPipeSession(IEnumerable<EventPipeProvider> providers, bool requestRundown=true, int circularBufferMB=256)
```

Starts an EventPipe tracing session using the given providers and settings. 

* `providers` : An `IEnumerable` of [`EventPipeProvider`](#class-eventpipeprovider)s to start tracing.
* `requestRundown`: A `bool` specifying whether rundown provider events from the target app's runtime should be requested.
* `circularBufferMB`: An `int` specifying the total size of circular buffer used by the target app's runtime on collecting events.


```csharp
public EventPipeSession StartEventPipeSession(EventPipeProvider providers, bool requestRundown=true, int circularBufferMB=256)
```

* `providers` : An [`EventPipeProvider`](#class-eventpipeprovider) to start tracing.
* `requestRundown`: A `bool` specifying whether rundown provider events from the target app's runtime should be requested.
* `circularBufferMB`: An `int` specifying the total size of circular buffer used by the target app's runtime on collecting events.


**Remarks**

Rundown events contain payloads that may be needed for post analysis, such as resolving method names of thread samples. Unless you know you do not want this, we recommend setting this to true. In large applications, this may take up to minutes.

* `circularBufferMB` : The size of the circular buffer to be used as a buffer for writing events within the runtime.



```csharp 
public void WriteDump(DumpType dumpType, string dumpPath=null, bool logDumpGeneration=false);
```

Request a dump for post-mortem debugging of the target application. The type of the dump can be specified using the [`DumpType`](#enum-dumptype) enum.

* `dumpType` : Type of the dump to be requested.

* `dumpPath` : The path to the dump to be written out to.
* `logDumpGeneration` : If set to `true`, the target application will write out diagnostic logs during dump generation.





```csharp
public void AttachProfiler(TimeSpan attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData=null);
```

Request to attach an ICorProfiler to the target application. 

* `attachTimeout` : A `TimeSpan` after which attach will be aborted.
* `profilerGuid` :  `Guid` of the ICorProfiler to be attached.
* `profilerPath `  : Path to the ICorProfiler dll to be attached.
* `additionalData` : Optional additional data that can be passed to the runtime during profiler attach.



```csharp
public static IEnumerable<int> GetPublishedProcesses();
```

Get an `IEnumerable` of process IDs of all the active .NET processes that can be attached to.





#### class EventPipeProvider

```cs
public class EventPipeProvider
{
    public EventPipeProvider(
        string name,
        EventLevel eventLevel,
        long keywords = 0,
        IDictionary<string, string> arguments = null)

    public string Name { get; }

    public EventLevel EventLevel { get; }

    public long Keywords { get; }

    public IDictionary<string, string> Arguments { get; }

    public override string ToString();

    public override bool Equals(object obj);

    public override int GetHashCode();

    public static bool operator ==(Provider left, Provider right);

    public static bool operator !=(Provider left, Provider right);
}
```



```csharp
public EventPipeProvider(string name,
                         EventLevel eventLevel,
                         long keywords = 0,
                         IDictionary<string, string> arguments = null)
```

Creates a new instance of `EventPipeProvider` with the given provider name, [EventLevel](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventlevel), keywords, and arguments. 

#### Properties



```csharp
public string Name { get; }
```

The name of the Provider



```csharp
public EventLevel EventLevel { get; }
```

The EventLevel of the given instance of [`EventPipeProvider`](#class-eventpipeprovider).



```csharp
public long Keywords { get; }
```

A long that represents bitmask for keywords of the EventSource. 



```csharp
public IDictionary<string, string> Arguments { get; }
```

An `IDictionary` of key-value pair string representing optional arguments to be passed to EventSource representing the given `EventPipeProvider`.


#### Remarks

This class is immutable, as EventPipe does not allow a provider's configuration to be modified during an EventPipe session (as of .NET Core 3.1).



### class EventPipeSession

```csharp
public class EventPipeSession : IDisposable
{
    public Stream EventStream { get; }
    
    public void Stop();
}
```

This class represents an ongoing EventPipe session that has been started. It is immutable and acts as a handle to an EventPipe session of the given runtime.

#### Properties

```csharp
public Stream EventStream { get; }
```

Returns a `Stream` that can be used to read the event stream.

#### Methods

```csharp
public void Stop();
```

Stops the given EventPipe session. 



### enum DumpType

```csharp
public enum DumpType
{
    Normal = 1,
    WithHeap = 2,
    Triage = 3,
    Full = 4
}
```

Represents the type of dump that can be requested.

* `Normal`: Include just the information necessary to capture stack traces for all existing traces for all existing threads in a process. Limited GC heap memory and information.
* `WithHeap`: Includes the GC heaps and information necessary to capture stack traces for all existing threads in a process.
* `Triage`: Include just the information necessary to capture stack traces for all existing traces for all existing threads in a process. Limited GC heap memory and information.
* `Full`: Include all accessible memory in the process. The raw memory data is included at the end, so that the initial structures can be mapped directly without the raw memory information. This option can result in a very large dump file.



### Exceptions

Either `DiagnosticsClientException` or its subclass can be thrown from the library.  

```csharp
public class DiagnosticsClientException : Exception
```

#### UnsupportedProtocolException

```csharp
public class UnsupportedProtocolException : DiagnosticsClientException
```

This may be thrown when the command is not supported by either the library or the target process' runtime. 



#### ServerNotAvailableException

```csharp
public class ServerNotAvailableException : DiagnosticsClientException
```

This may be thrown when the runtime is not available for diagnostics IPC commands, such as early during runtime startup before the runtime is ready for diagnostics commands, or when the runtime is shutting down.

#### ServerErrorException

```csharp
public class ServerErrorException : DiagnosticsClientException
```

This may be thrown when the runtime responds with an error to a given command.



