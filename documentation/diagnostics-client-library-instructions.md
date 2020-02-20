# Microsoft.Diagnostics.NETCore.Client API Documentation 

## Intro
Microsoft.Diagnostics.NETCore.Client (also known as the Diagnostics Client library) is a managed library that lets you interact with .NET Core runtime (CoreCLR) for various diagnostics related tasks, such as tracing, requesting a dump, or attaching an ICorProfiler.

## Installing 
Microsoft.Diagnostics.NETCore.Client is available on [NuGet](https://www.nuget.org/packages/Microsoft.Diagnostics.NETCore.Client/). 

## API Description

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





```csharp
public EventPipeSession StartEventPipeSession(IEnumerable<EventPipeProvider> providers, bool requestRundown=true, int circularBufferMB=256)
```

Starts an EventPipe tracing session using the given providers and settings. 

**Remarks** 

`requestRundown` specifies whether we should request for rundown provider events from the target app's runtime. These events contain payloads that may be needed for post analysis, such as resolving method names of thread samples. Unless you know you do not want this, we recommend setting this to true.





```csharp 
public void WriteDump(DumpType dumpType, string dumpPath=null, bool logDumpGeneration=false);
```

Request a dump for post-mortem debugging of the target application. The type of the dump can be specified using the [`DumpType`](#enum-dumptype) enum.





```csharp
public void AttachProfiler(TimeSpan attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData=null);
```

Request to attach an ICorProfiler to the target application. 





```csharp
public static IEnumerable<int> GetPublishedProcesses();
```

Get an `IEnumerable` of all active .NET processes that can be attached to.





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

An ```IDictionary``` of key-value pair string representing optional arguments to be passed to EventSource representing the given `EventPipeProvider`.



#### Methods

```csharp
public override string ToString();
```

Returns a string representation of the given `EventPipeProvider` instance.

```csharp
public override bool Equals(object obj);
```

Returns true if the given object is an instance of `EventPipeProvider` and represents the same provider configuration as the given instance.

```csharp
public override int GetHashCode();
```

Returns a hash of the given `EventPipeProvider` instance.

```csharp
public static bool operator ==(Provider left, Provider right);
```

Compares two `EventPipeProvider` instances and checks if they represent the same provider configuration.

```csharp
public static bool operator !=(Provider left, Provider right);
```

Compares two `EventPipeProvider` instances and checks if they are not the same provider configuration.



#### Remarks

This class is immutable, as currently EventPipe does not allow a provider's configuration to be modified during an EventPipe session. 



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

