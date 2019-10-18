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

## API Descriptions

At a high level, the DiagnosticsClient library provides a `CommandHandler` class for each of the command specified as part of the diagnostics IPC protocol described here: https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md#commands. For example, `EventPipeCommandHandler` handles all the `EventPipe` commands that are specified, such as `CollectTracing` and `CollectTracing2`. 

On top of this, there are helper methods that contain various util methods for talking to the IPC channel. These are included in the `DiagnosticsIPCHelper` class.

We may create additional namespaces under Microsoft.Diagnostics.Client for command-specific classes that may be useful. i.e. `Microsoft.Diagnostics.Client.EventPipe`.

### DiagnosticsIPCVersion
This is a helper class that encodes the minimum and maximum supported versions of the diagnostics IPC protocol. This helps the developer to programmatically opt in/out of certain features easily. Currently both min/max would be 1.

```cs
namespace Microsoft.Diagnostics.Client
{
    public class VersionInfo
    {
        public static const int MIN_SUPPORTED_VERSION;
        public static const int MAX_SUPPORTED_VERSION;
    }
}
```

### DiagnosticsCommand
This is an enum for all the diagnostics command supported in the current version of the library.
```cs
namespace Microsoft.Diagnostics.Client
{
    /// <summary>
    /// An enum for all the supported diagnostics IPC command
    /// </summary>
    public enum DiagnosticsCommand
    {
        Dump,
        EventPipe
        Profiler
    }
}
```

### DiagnosticsIPCHelper
This is a class that contains some utility methods for various purposes.
```cs
namespace Microsoft.Diagnostics.Client
    /// <summary>
    /// A utility class that contain various helper methods to interact with the diagnostics IPC.
    /// </summary>
    public class DiagnosticsIPCHelper
    {
        /// <summary>
        /// Get all the active processes that can be attached to.
        /// </summary>
        /// <returns>
        /// IEnumerable of all the active process IDs.
        /// </returns>
        public static IEnumerable<int> GetActiveProcesses()
    }
}
```

### EventPipeCommandHandler
This is a CommandHandler class for sending EventPipe commands across the diagnostics IPC channel.
```cs
namespace Microsoft.Diagnostics.Client
{
    /// <summary>
    /// A class for handling EventPipe commands.
    /// </summary>
    public class EventPipeCommandHandler
    {
        public ulong SessionId { get; }  /// A ulong representing the current session ID. Defaults to ulong.Max
        public uint CircularBufferSizeMB { get; set; }   /// The maximum buffer size in MB. Defaults to 
        public EventPipeSerializationFormat SerializationFormat { get; set; }  /// Serialization format. Defaults to NetTrace
        
        public EventPipeCommandHandler(int processId)

        /// <summary>
        /// Add a provider to the EventPipe command
        /// </summary>
        public void AddProvider(
            string name,
            ulong keywords=ulong.MaxValue,
            EventLevel=EventLevel.Verbose,
            string filterData=null)

        /// <summary>
        /// Start tracing the application via CollectTracing1 command.
        /// </summary> 
        /// <returns>
        /// A Stream object to read
        /// </returns> 
        public Stream CollectTracing1(SessionConfiguration configuration)

        /// <summary>
        /// Start tracing the application via CollectTracing2 command.
        /// </summary> 
        /// <returns>
        /// A Stream object to read
        /// </returns> 
        public Stream CollectTracing2()

        /// <summary>
        /// Stop tracing the applicating via the StopTracing command.
        /// </summary> 
        /// <returns>
        /// true if stopping the tracing session succeeded. false otherwise. 
        /// </returns> 
        public bool StopTracing()
    }
}
```

### DumpCommandHandler
This is a CommandHandler class for sending Dump commands across the diagnostics IPC channel.
```cs
    public class DumpCommandHandler
    {
        public DumpCommandHandler(int processId)
        public int GenerateCoreDump()
    }
```


### ProfilerCommandHandler
This is a CommandHandler class for sending Profiler commands across the diagnostics IPC channel.
```cs
    public class ProfilerCommandHandler
    {
        public ProfilerCommandHandler(int processId);
        public int AttachProfiler(uint attachTimeout, Guid profilerGuid, string profilerPath, byte[] additionalData);
    }
```


### Exceptions that can be thrown 
```cs
    namespace Microsoft.Diagnostics.Client.Exceptions
    {
        public class UnknownCommandException : Exception
        public class UnknownMagicException : Exception
        public class EventPipeInvalidArgumentException : Exception
        public class DumpInvalidArgumentException : Exception
        public class ProfilerInvalidArgumentException : Exception
    }
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
            ulong keywords = ulong.MaxValue,
            EventLevel eventLevel = EventLevel.Verbose,
            string filterData = null)

        public ulong Keywords { get; }

        public EventLevel EventLevel { get; }

        public string Name { get; }

        public string FilterData { get; }

        public string ToDisplayString() =>

        public override string ToString() =>
        
        public static bool operator ==(Provider left, Provider right)

        public static bool operator !=(Provider left, Provider right)

        public override bool Equals(object obj)

        public override int GetHashCode()
    }
}
```
## Sample Code:


### 1. Attaching to a process and dumping out its event data 
```cs

public static void Main(String[] args)
{
    int processId = Int32.Parse(args[0]);
    
    // Create an EventPipe command handler for the given process ID
    EventPipeCommandHandler handler = new EventPipeCommandHandler(processId);

    // Add providers that should be turned on
    handler.AddProvider("Microsoft-Windows-DotNETRuntime", 0x1, EventLevel.Informational);
    handler.AddProvider("System.Runtime", 0x1, EventLevel.Informational, "EventCounterIntervalSec=1");

    // Start tracing
    Stream stream = handler.CollectTracing1();
    
    // Use TraceEvent to read & parse the stream here.
}
```

// TODO: MORE TO COME

