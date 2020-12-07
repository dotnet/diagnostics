# Diagnostics Client Library API Design 

*NOTE*

This page is meant to serve as a design doc for Microsoft.Diagnostics.NETCore.Client and may contain features or APIs that are still work in progress. To see sample usage and API docs on supported/published builds of the library, refer to the [official documentation page](https://docs.microsoft.com/en-us/dotnet/core/diagnostics/microsoft-diagnostics-netcore-client).

## API Descriptions

At a high level, the DiagnosticsClient class provides static methods that the user may call to invoke diagnostics IPC commands (i.e. start an EventPipe session, request a core dump, etc.) The library also provides several classes that may be helpful for invoking these commands. These commands are described in more detail in the diagnostics IPC protocol documentation available here: https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md#commands. 


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

