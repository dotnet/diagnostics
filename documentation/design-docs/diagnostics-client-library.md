# Diagnostics Client Library API Design 

## Intro
The Diagnostics Client Library (currently named as "Runtime Client Library") is a managed library that can be used to interact with the .NET runtime via the diagnostics IPC protocol as documented in https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md.

### Name
The name "Diagnostics Client Library" comes from the fact that we call the runtime (CoreCLR) component responsible for accepting and handling the diagnostics IPC commands the "diagnostics server" - https://github.com/dotnet/coreclr/blob/master/src/vm/diagnosticserver.h. Since this library is a managed library on the other side of the IPC protocol responsible for communicating with the runtime's "diagnostics server", calling this the "Diagnostics Client Library" made sense.

### High Level Overview


## Goals

The goal of this library is as following:

* Serve as an implementation of the IPC protocol to communicate with CoreCLR's diagnostics server.
* Provide an easy-to-use API for any library/tools authors to utilize the IPC protocol 

## Non-Goals

* Provide tool-specific functionalities that are too high-level (i.e. dumping the GC heap, parsing counter payload, etc.)

## API Descriptions

At a high level, the DiagnosticsClient library provides a `CommandHandler` class for each of the command specified as part of the diagnostics IPC protocol described here: https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md#commands. For example, `EventPipeCommandHandler` handles all the `EventPipe` commands that are specified, such as `CollectTracing` and `CollectTracing2`. 

On top of this, there are helper methods that contain various util methods for talking to the IPC channel. These are included in the `DiagnosticsIPCHelper` class.

We may create additional namespaces under Microsoft.Diagnostics.Client for command-specific classes that may be useful. i.e. `Microsoft.Diagnostics.Client.EventPipe`.

* API Surface:

## Microsoft.Diagnostics.Client namespace:
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
        
        public Stream CollectTracingV1(SessionConfiguration configuration)

        public Stream CollectTracingV2()

        public bool StopTracing()
    }

    public class DumpCommandHandler
    {
        public DumpCommandHandler(int processId)
        public static int GenerateCoreDump()
    }

    public class ProfilerCommandHandler
    {
        
    }


    namespace Microsoft.Diagnostics.Client.Exceptions
    {
        public class UnknownCommandException : Exception

        public class UnknownMagicException : Exception
        
        public class 
    }

}
```

## Micorosft.Diagnostics.Client.EventPipe
``` cs
namespace Micorosft.Diagnostics.Client.EventPipe
{
    public enum EventPipeCommandSet
    {
        StopTracing,
        CollectTracing,
        CollectTracing2
    }

    public enum EventPipeSerializationFormat
    {
        NetPerf,
        NetTrace
    }

    public class EventPipeProvider
    {
        
    }
}
```
## Sample Code:

```
