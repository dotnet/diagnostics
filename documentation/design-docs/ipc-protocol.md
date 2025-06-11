# Diagnostic IPC Protocol

## Overview

This spec describes the IPC Protocol to be used for communicating with the dotnet core runtime's Diagnostics Server from an external client over a platform-specific transport, e.g., Unix Domain Sockets.


### Terminology

The protocol will use the following names for various constructs and behaviors defined in this spec:
* *Diagnostic IPC Protocol*: The protocol defined in this spec
* *Diagnostic Server*: The server in the runtime that receives/sends Diagnostic IPC Procotol communication.
* *Commands*: The functionality being invoked in the runtime that communicates over the Diagnostic IPC Protocol, e.g., "Start an EventPipe stream".  These are encoded as a `command_set` and a `command_id`.
* *Flow*: A sequence of interactions making up communication with the Diagnostics Server
* *Pipe*: The duplex communication construct this protocol is communicated over.  This is a Unix Domain Socket on *nix systems and a Named Pipe on Windows.
* *IPC Message*: The base unit of communication over the Diagnostic IPC Protocol. Is made up of a Header and a Payload.
* *Header*: A struct containing a magic version, the size, a command, and metadata.
* *Payload*: An opaque chunk of data that is Command specific.
* *Optional Continuation*: The reuse of the pipe for application specific communication. This communication does not need to adhere to any requirements listed in this spec, e.g., this could be a stream of custom encoded data that is Command specific.

## General Flow

All communication with the Diagnostic Server will begin with a Diagnostic IPC Message sent from the client to the server.  The server will respond with a Diagnostic IPC Message.  After this, the client and runtime _may_ reuse the Pipe for any Command specific communication which is referred to as an Optional Continuation.

![Generic Flow](ipc-protocol-genericflow.svg)

```
runtime <- client : [ Header ][ Payload ]
runtime -> client : [ Header ][ Payload ]
optional:
    runtime <-> client : [ Optional Continuation ]
connection closed
```

Example flow for EventPipe:
```
runtime <- client : [ magic; size; EventPipe CollectTracing ][ stream config struct  ] <- Diagnostic IPC Message
runtime -> client : [ magic; size; Server OK                ][ sessionId             ] <- Diagnostic IPC Message
runtime -> client : [ stream of nettrace data ]                                        <- Optional Continuation

// stop message is sent on another connection

connection closed
```

## Transport

The protocol will be communicated over a platform-specific transport.  On Unix/Linux based platforms, a Unix Domain Socket will be used, and on Windows, a Named Pipe will be used.

#### Naming and Location Conventions

Unix Domain Sockets (MacOS and *nix):

The socket is placed in one of two places:
1. The directory specified in `$TMPDIR`
2. `/tmp` if `$TMPDIR` is undefined/empty

In order to ensure filename uniqueness, a `disambiguation key` is generated.  On Mac and NetBSD, this is the process start time encoded as the number of seconds since UNIX epoch time.  If `/proc/$PID/stat` is available (all other *nix platforms), then the process start time encoded as jiffies since boot time is used.

> NOTE: If the target application is running inside an application sandbox on MacOS, the transport will be placed in the Application Group container directory.  This is a convention for all sandboxed applications on MacOS.

socket name:
```c
dotnet-diagnostic-{%d:PID}-{%llu:disambiguation key}-socket
```

Named Pipes (Windows):
```
\\.\pipe\dotnet-diagnostic-{%d:PID}
```

## Messages

Diagnostic IPC Messages are the base unit of communication with the Diagnostic Server.  A Diagnostic IPC Message contains a Header and Payload (described in following sections).

<table>
  <tr>
    <th>1</th>
    <th>2</th>
    <th>3</th>
    <th>4</th>
    <th>5</th>
    <th>6</th>
    <th>7</th>
    <th>8</th>
    <th>9</th>
    <th>10</th>
    <th>11</th>
    <th>12</th>
    <th>13</th>
    <th>14</th>
    <th>15</th>
    <th>16</th>
    <th>17</th>
    <th>18</th>
    <th>19</th>
    <th>20</th>
    <th>21</th>
    <th>22</th>
    <th>23</th>
    <th>24</th>
    <th>...</th>
    <th>size - 1 </th>
    <th>size</th>
  </tr>
  <tr>
    <td colspan="20">header</td>
    <td colspan="7">payload</td>
  </tr>
  <tr>
    <td colspan="14">magic</td>
    <td colspan="2">size</td>
    <td colspan="1">command_set</td>
    <td colspan="1">command_id</td>
    <td colspan="2">reserved</td>
    <td colspan="7">payload</td>
  </tr>
</table>

The simplest Diagnostic IPC Message will contain a Header and an empty Payload and therefore only be 20 bytes long.

For example, this IPC Message is the generic OK message which has an empty Payload:
<table>
  <tr>
    <th>1</th>
    <th>2</th>
    <th>3</th>
    <th>4</th>
    <th>5</th>
    <th>6</th>
    <th>7</th>
    <th>8</th>
    <th>9</th>
    <th>10</th>
    <th>11</th>
    <th>12</th>
    <th>13</th>
    <th>14</th>
    <th>15</th>
    <th>16</th>
    <th>17</th>
    <th>18</th>
    <th>19</th>
    <th>20</th>
  </tr>
  <tr>
    <tr>
    <td colspan="14">magic</td>
    <td colspan="2">size</td>
    <td colspan="2">command</td>
    <td colspan="2">reserved</td>
  </tr>
  <tr>
    <td colspan="14">"DOTNET_IPC_V1"</td>
    <td colspan="2">20</td>
    <td colspan="1">0xFF</td>
    <td colspan="1">0x00</td>
    <td colspan="2">0x0000</td>
  </tr>
</table>

### Headers

Every Diagnostic IPC Message will start with a header and every header will:
* start with a magic version number and a size
* `sizeof(IpcHeader) == 20`
* encode numbers little-endian
* account for the size of the payload in the `size` value, i.e., `IpcHeader.size == sizeof(IpcHeader) + PayloadStruct.GetSize()`

```c
// size = 14 + 2 + 1 + 1 + 2 = 20 bytes
struct IpcHeader
{
    uint8_t[14]  magic = "DOTNET_IPC_V1";
    uint16_t     size;        // size of packet = size of header + payload
    uint8_t      command_set; // combined with command_id is the Command to invoke
    uint8_t      command_id;  // combined with command_set is the Command to invoke
    uint16_t     reserved;    // for potential future use
};
```

The `reserved` field is reserved for future use.  It is unused in `DOTNET_IPC_V1` and must be 0x0000.


### Payloads

Payloads are Command specific data encoded into a Diagnostic IPC Message.  The size of the payload is implicitly encoded in the Header's `size` field as `PayloadSize = header.size - sizeof(struct IpcHeader)`.  A Payload _may_ be 0 bytes long if it empty.  The encoding of data in the Payload is Command specific.

Payloads are either encoded as fixed size structures that can be `memcpy`'ed , _or_:

* `X, Y, Z` means encode bytes for `X` followed by bytes for `Y` followed by bytes for `Z`
* `uint` = 4 little endian bytes
* `ulong` = 8 little endian bytes
* `wchar` = 2 little endian bytes, UTF16 encoding
* `bool` = 1 unsigned byte
* `array<T>` = uint length, length # of `T`s
* `string` = (`array<wchar>` where the last `wchar` must = `0`) or (length = `0`)

As an example, the [CollectTracing](#collecttracing) command to EventPipe encodes its Payload as:

<table>
  <tr>
    <td colspan="14">1 - 14</td>
    <td colspan="2">15 - 16</td>
    <td colspan="2">17 - 18</td>
    <td colspan="2">19 - 20</td>
    <td colspan="4">21 - 24</td>
    <td colspan="4">25 - 28</td>
    <td colspan="4">29 - 32</td>
    <td colspan="8">33 - 40</td>
    <td colspan="4">41 - 44</td>
    <td colspan="4">45 - 48</td>
    <td colspan="28">49 - 76</td>
    <td colspan="4">77 - 80</td>
  </tr>
  <tr>
    <td colspan="20">Header</td>
    <td colspan="60">Payload</td>
  </tr>
  <tr>
    <td colspan="14">magic</td>
    <td colspan="2">size</td>
    <td colspan="2">command</td>
    <td colspan="2">reserved</td>
    <td colspan="4">circularBufferMB</td>
    <td colspan="4">format</td>
    <td colspan="4">n Providers</td>
    <td colspan="8">Keywords</td>
    <td colspan="4">logLevel</td>
    <td colspan="4">provider_name length</td>
    <td colspan="28">provider_name string</td>
    <td colspan="4">arguments length</td>
  </tr>
  <tr>
    <td colspan="14">"DOTNET_IPC_V1"</td>
    <td colspan="2">80</td>
    <td colspan="2">0x0202</td>
    <td colspan="2">0x0000</td>
    <td colspan="4">250</td>
    <td colspan="4">1</td>
    <td colspan="4">1</td>
    <td colspan="8">100</td>
    <td colspan="4">2</td>
    <td colspan="4">14</td>
    <td colspan="28">"MyEventSource"</td>
    <td colspan="4">0</td>
  </tr>
</table>

Where `0x0202` is the Command to start streaming with EventPipe.

### Commands

Commands are a `command_set` and a `command_id`.  A `command_set` is analogous to a namespace for `command_id`s.  The typical grouping is by service running on the Diagnostic Server, e.g., there is a `command_set` for EventPipe.  This allows multiple services to have the same `command_id`s without clashing.  The combination of a `command_set` and a `command_id` encodes the Command being invoked on the Diagnostic Server.

The current set of `command_set`s and `command_id`s are listed below:

```c++
enum class CommandSet : uint8_t
{
    // reserved = 0x00,
    Dump        = 0x01,
    EventPipe   = 0x02,
    Profiler    = 0x03,
    Process     = 0x04,
    // future

    Server = 0xFF,
};
```

```c++
enum class ServerCommandId : uint8_t
{
    OK    = 0x00,
    Error = 0xFF,
};
```

```c++
enum class EventPipeCommandId : uint8_t
{
    // reserved = 0x00,
    StopTracing     = 0x01, // stop a given session
    CollectTracing  = 0x02, // create/start a given session
    CollectTracing2 = 0x03, // create/start a given session with/without rundown
    CollectTracing3 = 0x04, // create/start a given session with/without collecting stacks
    CollectTracing4 = 0x05, // create/start a given session with specific rundown keyword
    CollectTracing5 = 0x06, // create/start a given session with/without user_events
}
```
See: [EventPipe Commands](#EventPipe-Commands)

```c++
enum class DumpCommandId : uint8_t
{
    // reserved     = 0x00,
    GenerateCoreDump  = 0x01,
    GenerateCoreDump2  = 0x02,
    GenerateCoreDump3  = 0x03,
    // future
}
```
See: [Dump Commands](#Dump-Commands)

```c++
enum class ProfilerCommandId : uint8_t
{
    // reserved     = 0x00,
    AttachProfiler  = 0x01,
    StartupProfiler = 0x02,
    // future
}
```
See: [Profiler Commands](#Profiler-Commands)

```c++
enum class ProcessCommandId : uint8_t
{
    ProcessInfo            = 0x00,
    ResumeRuntime          = 0x01,
    ProcessEnvironment     = 0x02,
    SetEnvironmentVariable = 0x03,
    ProcessInfo2           = 0x04,
    EnablePerfMap          = 0x05,
    DisablePerfMap         = 0x06,
    ApplyStartupHook       = 0x07
    ProcessInfo3           = 0x08,
    // future
}
```
See: [Process Commands](#Process-Commands)

Commands may use the generic `{ magic="DOTNET_IPC_V1"; size=20; command_set=0xFF (Server); command_id=0x00 (OK); reserved = 0x0000; }` to indicate success rather than having a command specific success `command_id`.

For example, the Command to start a stream session with EventPipe would be `0x0202` made up of `0x02` (the `command_set` for EventPipe) and `0x02` (the `command_id` for CollectTracing).

## EventPipe Commands

### EventPipe Command IDs
```c++
enum class EventPipeCommandId : uint8_t
{
    // reserved = 0x00,
    StopTracing     = 0x01, // stop a given session
    CollectTracing  = 0x02, // create/start a given session
    CollectTracing2 = 0x03, // create/start a given session with/without rundown
    CollectTracing3 = 0x04, // create/start a given session with/without collecting stacks
    CollectTracing4 = 0x05, // create/start a given session with specific rundown keyword
    CollectTracing5 = 0x06, // create/start a given session with/without user_events
}
```
EventPipe Payloads are encoded with the following rules:

* `X, Y, Z` means encode bytes for `X` followed by bytes for `Y` followed by bytes for `Z`
* `uint` = 4 little endian bytes
* `ulong` = 8 little endian bytes
* `wchar` = 2 little endian bytes, UTF16 encoding
* `byte` = 1 unsigned byte
* `bool` = 1 unsigned byte
* `array<T>` = uint length, length # of `T`s
* `string` = (`array<wchar>` where the last `wchar` must = `0`) or (length = `0`)

### `StopTracing`

Command Code: `0x0201`

The `StopTracing` command is used to stop a specific EventPipe session.  Clients are expected to use this command to stop EventPipe sessions started with [`CollectStreaming`](#CollectStreaming).

#### Inputs:

Header: `{ Magic; 28; 0x0201; 0x0000 }`

Payload:
* `ulong sessionId`: The ID for the EventPipe session to stop

#### Returns:

Header: `{ Magic; 28; 0xFF00; 0x0000 }`

Payload:
* `ulong sessionId`: the ID for the EventPipe session that was stopped


##### Details:

Inputs:
```c
Payload
{
   ulong sessionId
}
```

Returns:
```c
Payload
{
   ulong sessionId
}
```

### `CollectTracing`

Command Code: `0x0202`

The `CollectTracing` Command is used to start a streaming session of event data.  The runtime will attempt to start a session and respond with a success message with a payload of the `sessionId`.  The event data is streamed in the `nettrace` format.  The stream begins after the response Message from the runtime to the client.  The client is expected to continue to listen on the transport until the connection is closed.

In the event there is an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

The client is expected to send a [`StopTracing`](#StopTracing) command to the runtime in order to stop the stream, as there is a "run down" at the end of a stream session that transmits additional metadata.

If the stream is stopped prematurely due to a client or server error, the `nettrace` file generated will be incomplete and should be considered corrupted.

#### Inputs:

Header: `{ Magic; 20 + Payload Size; 0x0202; 0x0000 }`

Payload:
* `uint circularBufferMB`: The size of the circular buffer used for buffering event data while streaming
* `uint format`: 0 for the legacy NetPerf format and 1 for the NetTrace V4 format
* `array<provider_config> providers`: The providers to turn on for the streaming session

A `provider_config` is composed of the following data:
* `ulong keywords`: The keywords to turn on with this providers
* `uint logLevel`: The level of information to turn on
* `string provider_name`: The name of the provider
* `string arguments`: (Key-value pairs string to pass to the provider) or (length = 0)

> see ETW documentation for a more detailed explanation of Keywords, Filters, and Log Level.

#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`CollectTracing` returns:
* `ulong sessionId`: the ID for the stream session starting on the current connection

##### Details:

Input:
```
Payload
{
    uint circularBufferMB,
    uint format,
    array<provider_config> providers
}

provider_config
{
    ulong keywords,
    uint logLevel,
    string provider_name,
    string arguments
}
```

Returns:
```c
Payload
{
    ulong sessionId
}
```
Followed by an Optional Continuation of a `nettrace` format stream of events.

### `CollectTracing2`

Command Code: `0x0203`

The `CollectTracing2` command is an extension of the `CollectTracing` command - its behavior is the same as `CollectTracing` command, except that it has another field that lets you specify whether rundown events should be fired by the runtime.

#### Inputs:

Header: `{ Magic; 20 + Payload Size; 0x0203; 0x0000 }`

* `uint circularBufferMB`: The size of the circular buffer used for buffering event data while streaming
* `uint format`: 0 for the legacy NetPerf format and 1 for the NetTrace V4 format
* `bool requestRundown`: Indicates whether rundown should be fired by the runtime.
* `array<provider_config> providers`: The providers to turn on for the streaming session

A `provider_config` is composed of the following data:
* `ulong keywords`: The keywords to turn on with this providers
* `uint logLevel`: The level of information to turn on
* `string provider_name`: The name of the provider
* `string arguments`: (Key-value pairs string to pass to the provider) or (length = 0)

> see ETW documentation for a more detailed explanation of Keywords, Filters, and Log Level.
>
#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`CollectTracing2` returns:
* `ulong sessionId`: the ID for the stream session starting on the current connection

##### Details:

Input:
```
Payload
{
    uint circularBufferMB,
    uint format,
    bool requestRundown,
    array<provider_config> providers
}

provider_config
{
    ulong keywords,
    uint logLevel,
    string provider_name,
    string arguments
}
```

Returns:
```c
Payload
{
    ulong sessionId
}
```
Followed by an Optional Continuation of a `nettrace` format stream of events.

### `CollectTracing3`

Command Code: `0x0204`

The `CollectTracing3` command is an extension of the `CollectTracing2` command - its behavior is the same as `CollectTracing2` command, except that it has another field that lets you specify whether the stackwalk should be made for each event.

#### Inputs:

Header: `{ Magic; 20 + Payload Size; 0x0203; 0x0000 }`

* `uint circularBufferMB`: The size of the circular buffer used for buffering event data while streaming
* `uint format`: 0 for the legacy NetPerf format and 1 for the NetTrace V4 format
* `bool requestRundown`: Indicates whether rundown should be fired by the runtime.
* `bool requestStackwalk`: Indicates whether stacktrace information should be recorded.
* `array<provider_config> providers`: The providers to turn on for the streaming session

A `provider_config` is composed of the following data:
* `ulong keywords`: The keywords to turn on with this providers
* `uint logLevel`: The level of information to turn on
* `string provider_name`: The name of the provider
* `string arguments`: (Key-value pairs string to pass to the provider) or (length = 0)

> see ETW documentation for a more detailed explanation of Keywords, Filters, and Log Level.
>
#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`CollectTracing3` returns:
* `ulong sessionId`: the ID for the stream session starting on the current connection

##### Details:

Input:
```
Payload
{
    uint circularBufferMB,
    uint format,
    bool requestRundown,
    bool requestStackwalk,
    array<provider_config> providers
}

provider_config
{
    ulong keywords,
    uint logLevel,
    string provider_name,
    string arguments
}
```

Returns:
```c
Payload
{
    ulong sessionId
}
```
Followed by an Optional Continuation of a `nettrace` format stream of events.

### `CollectTracing4`

Command Code: `0x0205`

The `CollectTracing4` command is an extension of the `CollectTracing3` command - its behavior is the same as `CollectTracing3` command, except the requestRundown field is replaced by the rundownKeyword field to allow customizing the set of rundown events to be fired.

A rundown keyword of `0x80020139` has the equivalent behavior as `CollectTracing3` with `requestRundown=true` and rundown keyword of `0` has the equivalent behavior as `requestRundown=false`.


> Note available for .NET 9.0 and later.

#### Inputs:

Header: `{ Magic; 20 + Payload Size; 0x0205; 0x0000 }`

Payload:
* `uint circularBufferMB`: The size of the circular buffer used for buffering event data while streaming
* `uint format`: 0 for the legacy NetPerf format and 1 for the NetTrace V4 format
* `ulong rundownKeyword`: Indicates the keyword for the rundown provider
* `bool requestStackwalk`: Indicates whether stacktrace information should be recorded.
* `array<provider_config> providers`: The providers to turn on for the streaming session

A `provider_config` is composed of the following data:
* `ulong keywords`: The keywords to turn on with this providers
* `uint logLevel`: The level of information to turn on
* `string provider_name`: The name of the provider
* `string arguments`: (Key-value pairs string to pass to the provider) or (length = 0)

> see ETW documentation for a more detailed explanation of Keywords, Filters, and Log Level.
>
#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`CollectTracing4` returns:
* `ulong sessionId`: the ID for the stream session starting on the current connection

##### Details:

Input:
```
Payload
{
    uint circularBufferMB,
    uint format,
    ulong rundownKeyword
    array<provider_config> providers
}

provider_config
{
    ulong keywords,
    uint logLevel,
    string provider_name,
    string arguments
}
```

Returns:
```c
Payload
{
    ulong sessionId
}
```
Followed by an Optional Continuation of a `nettrace` format stream of events.

### `CollectTracing5`

Command Code: `0x0206`

The `CollectTracing5` command is an extension of the `CollectTracing4` command. It has all the capabilities of `CollectTracing4` and introduces new fields to enable a Linux-only user_events-based eventpipe session and to prescribe an enable/disable list for Event IDs. When the user_events-based eventpipe session is enabled, the file descriptor and SCM_RIGHTS of the `user_events_data` file must be sent through the optional continuation stream as [described](#passing_file_descriptor). The runtime will register tracepoints based on the provider configurations passed in, and runtime events will be written directly to the `user_events_data` file descriptor. The enable/disable list of Event IDs will apply after the keyword/level filter to determine whether or not that provider's event will be written.

> Note available for .NET 10.0 and later.

#### Inputs:

Header: `{ Magic; 20 + Payload Size; 0x0206; 0x0000 }`

#### Streaming Session Payload:
* `uint session_type`: 0
* `uint streaming_circularBufferMB`: Specifies the size of the Streaming session's circular buffer used for buffering event data.
* `uint streaming_format`: 0 for the legacy NetPerf format and 1 for the NetTrace V4 format. Specifies the format in which event data will be serialized into the IPC Stream
* `ulong rundownKeyword`: Indicates the keyword for the rundown provider
* `bool requestStackwalk`: Indicates whether stacktrace information should be recorded.
* `array<streaming_provider_config> providers`: The providers to turn on for the session

The `streaming_provider_config` is composed of the following data:
* `ulong keywords`: The keywords to turn on with this provider
* `uint logLevel`: The level of information to turn on
* `string provider_name`: The name of the provider
* `string arguments`: (Key-value pairs string to pass to the provider) or (length = 0)
* `event_filter filter`: Rules for filtering this provider's Event IDs, applied after `keyword`/`logLevel`, using an enable/disable list or (length = `0`).

An `event_filter` is comprised of the following data:
* `bool enable`: 0 to disable events, 1 to enable events
* `array<uint> event_ids`: List of Event IDs to disable or enable.

See [event_filter serialization examples](#event_filter)

#### User_events Session Payload:
* `uint session_type`: 1
* `ulong rundownKeyword`: Indicates the keyword for the rundown provider
* `array<user_events_provider_config> providers`: The providers to turn on for the session

The `user_events_provider_config` is composed of the following data:
* `ulong keywords`: The keywords to turn on with this provider
* `uint logLevel`: The level of information to turn on
* `string provider_name`: The name of the provider
* `string arguments`: (Key-value pairs string to pass to the provider) or (length = 0)
* `event_filter filter`: Rules for filtering this provider's Event IDs, applied after `keyword`/`logLevel`, using an enable/disable list or (length = `0`).
* `tracepoint_config config`: Maps Event IDs to tracepoints. If an Event ID is excluded by `event_filter`, it will not be written to any tracepoint.

An `event_filter` is comprised of the following data:
* `bool enable`: 0 to disable events, 1 to enable events
* `array<uint> event_ids`: List of Event IDs to disable or enable.

See [event_filter serialization examples](#event_filter)

A `tracepoint_config` is comprised of the following data:
* `string default_tracepoint_name`: (The default tracepoint filtered Event IDs will be written to unless otherwise specified by `tracepoints`) or (length = `0` to only write to tracepoints specified in `tracepoints`)
* `array<tracepoint_set> tracepoints`: Specifies alternate tracepoints for a set of Event IDs to be written to instead of the default tracepoint or (length = `0`).

A `tracepoint_set` is comprised of the following data:
* `string tracepoint_name`: The tracepoint that the following subset of Event IDs should be written to.
* `array<uint> event_ids`: The Event IDs to be written to `tracepoint_name`.

With a user_events session, atleast one of `default_tracepoint_name` and `tracepoints` must be specified. An error will be returned through the stream if both are length = `0`.
Event IDs specified in `tracepoint_set`s must be exclusive. If an Event ID is detected in different `tracepoint_set`s of the provider, an error will be returned through the stream.

See [tracepoint_config serialization examples](#tracepoint_config)

> See ETW documentation for a more detailed explanation of Keywords, Filters, and Log Level.

#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`CollectTracing5` returns:
* `ulong sessionId`: the ID for the EventPipe Session started

A Streaming Session started with `CollectTracing5` is followed by an Optional Continuation of a `nettrace` format stream of events.

A User_events Session started with `CollectTracing5` expects the Optional Continuation to contain another message passing along the SCM_RIGHTS `user_events_data` file descriptor. See [details](#passing_file_descriptor)

## EventPipe Payload Serialization Examples

### Event_filter
Example `event_filter` serialization. Serializing
```
enable=0, event_ids=[]
Disable Nothing === Enable all events.
```
<table>
  <tr>
    <th>1</th>
    <th>2-5</th>
  </tr>
  <tr>
    <tr>
    <td colspan="1">bool</td>
    <td colspan="4">array&ltuint&gt</td>
  </tr>
  <tr>
    <tr>
    <td colspan="1">enable</td>
    <td colspan="1">event_ids</td>
  </tr>
  <tr>
    <td colspan="1">0</td>
    <td colspan="1">0</td>
  </tr>
</table>

```
enable=0, event_ids=[4, 5]
Disable only Event IDs 4 and 5 === Enable all Event IDs except 4 and 5
```

<table>
  <tr>
    <th>1</th>
    <th>2</th>
    <th>6</th>
    <th>10-13</th>
  </tr>
  <tr>
    <td colspan="1">bool</td>
    <td colspan="4">array&ltuint&gt</td>
  </tr>
  <tr>
    <td colspan="1">enable</td>
    <td colspan="4">event_ids</td>
  </tr>
  <tr>
    <td colspan="1">0</td>
    <td colspan="1">2</td>
    <td colspan="1">4</td>
    <td colspan="1">5</td>
  </tr>
</table>

```
enable=1, event_ids=[]
Enable Nothing === Disable all events.
```

<table>
  <tr>
    <th>1</th>
    <th>2-5</th>
  </tr>
  <tr>
    <tr>
    <td colspan="1">bool</td>
    <td colspan="4">array&ltuint&gt</td>
  </tr>
  <tr>
    <tr>
    <td colspan="1">enable</td>
    <td colspan="1">event_ids</td>
  </tr>
  <tr>
    <td colspan="1">1</td>
    <td colspan="1">0</td>
  </tr>
</table>

```
enable=1, event_ids=[1, 2, 3]
Enable only EventIDs 1, 2, and 3 === Disable all EventIDs except 1, 2, and 3.
```

<table>
  <tr>
    <th>1</th>
    <th>2</th>
    <th>6</th>
    <th>10</th>
    <th>14-17</th>
  </tr>
  <tr>
    <tr>
    <td colspan="1">bool</td>
    <td colspan="4">array&ltuint&gt</td>
  </tr>
  <tr>
    <tr>
    <td colspan="1">enable</td>
    <td colspan="4">event_ids</td>
  </tr>
  <tr>
    <td colspan="1">1</td>
    <td colspan="1">3</td>
    <td colspan="1">1</td>
    <td colspan="1">2</td>
    <td colspan="1">3</td>
  </tr>
</table>

### Tracepoint_config
Example `tracepoint_config` serialization
```
session_type=0, Streaming Sessions DO NOT encode bytes for tracepoint_config
session_type=1, encode bytes for tracepoint_config
```

```
All enabled Event IDs will be written to a default "MyTracepoint" tracepoint
```
<table>
  <tr>
    <th>1</th>
    <th>5</th>
    <th>33-36</th>
  </tr>
  <tr>
    <tr>
    <td colspan="2">string (array&ltwchar&gt)</td>
    <td colspan="1">array&ltuint&gt</td>
  </tr>
  <tr>
    <td colspan="2">default_tracepoint_name</td>
    <td colspan="1">tracepoints</td>
  </tr>
  <tr>
    <td colspan="1">14</td>
    <td colspan="1">"MyTracepoint"</td>
    <td colspan="1">0</td>
  </tr>
</table>

```
Enabled Event IDs 1 - 9 will be written to tracepoint "LowEvents".
All other enabled Event IDs will be written to "MyTracepoint"
```
<table>
  <tr>
    <th>1</th>
    <th>5</th>
    <th>33</th>
    <th>37</th>
    <th>41</th>
    <th>61</th>
    <th>65</th>
    <th>69</th>
    <th>73</th>
    <th>77</th>
    <th>81</th>
    <th>85</th>
    <th>89</th>
    <th>93</th>
    <th>97-100</th>
  </tr>
  <tr>
    <tr>
      <td colspan="2">string (array&ltwchar&gt)</td>
      <td colspan="1">uint</td>
      <td colspan="2">string (array&ltwchar&gt)</td>
      <td colspan="10">array&lt;uint&gt;</td>
    </tr>
  </tr>
  <tr>
    <tr>
    <td colspan="2">default_tracepoint_name</td>
    <td colspan="1">tracepoints</td>
    <td colspan="2">tracepoint_name</td>
    <td colspan="10">event_ids</td>
  </tr>
  <tr>
    <td colspan="1">14</td>
    <td colspan="1">"MyTracepoint"</td>
    <td colspan="1">1</td>
    <td colspan="1">10</td>
    <td colspan="1">"LowEvents"</td>
    <td colspan="1">9</td>
    <td colspan="1">1</td>
    <td colspan="1">2</td>
    <td colspan="1">3</td>
    <td colspan="1">4</td>
    <td colspan="1">5</td>
    <td colspan="1">6</td>
    <td colspan="1">7</td>
    <td colspan="1">8</td>
    <td colspan="1">9</td>
  </tr>
</table>

```
Enabled Event IDs 1 - 9 will be written to tracepoint "LowEvents".
No default tracepoint needed, don't write any other enabled Event IDs
```
<table>
  <tr>
    <th>1</th>
    <th>5</th>
    <th>9</th>
    <th>13</th>
    <th>33</th>
    <th>37</th>
    <th>41</th>
    <th>45</th>
    <th>49</th>
    <th>53</th>
    <th>57</th>
    <th>61</th>
    <th>65</th>
    <th>69-72</th>
  </tr>
  <tr>
    <tr>
      <td colspan="1">string (array&ltwchar&gt)</td>
      <td colspan="1">uint</td>
      <td colspan="2">string (array&ltwchar&gt)</td>
      <td colspan="10">array&lt;uint&gt;</td>
    </tr>
  </tr>
  <tr>
    <tr>
    <td colspan="1">default_tracepoint_name</td>
    <td colspan="1">tracepoints</td>
    <td colspan="2">tracepoint_name</td>
    <td colspan="10">event_ids</td>
  </tr>
  <tr>
    <td colspan="1">0</td>
    <td colspan="1">1</td>
    <td colspan="1">10</td>
    <td colspan="1">"LowEvents"</td>
    <td colspan="1">9</td>
    <td colspan="1">1</td>
    <td colspan="1">2</td>
    <td colspan="1">3</td>
    <td colspan="1">4</td>
    <td colspan="1">5</td>
    <td colspan="1">6</td>
    <td colspan="1">7</td>
    <td colspan="1">8</td>
    <td colspan="1">9</td>
  </tr>
</table>

### passing_file_descriptor

> Note: This only applies to enabling an user_event-based EventPipe session, which is specifically a Linux feature

To register [user_event](https://docs.kernel.org/trace/user_events.html) tracepoints and write events, access to the root protected `user_events_data` file is required. Once the .NET Runtime's Diagnostic Server processes a [CollectTracing5](#collecttracing5) command specifying the `user_events` format (`session_type=1`), it expects that the client will send a file descriptor to the [continuation stream](#general-flow) via SCM_RIGHTS.

```C
#include <sys/socket.h>
#include <sys/un.h>

struct msghdr {
    void * msg_name;             /* ignored by runtime */
    unsigned int msg_namelen;    /* ignored by runtime */
    struct iovec * msg_iov;      /* runtime will "parse" 1 byte */
    unsigned int msg_iovlen;     /* runtime will "parse" one msg_iov */
    void * msg_control;          /* ancillary data */
    unsigned int msg_controllen; /* ancillary data buffer len */
    int msg_flags;               /* ignored by runtime */
};

struct cmsghdr {
    unsigned int cmsg_len;     /* length of control message */
    int cmsg_level;            /* SOL_SOCKET */
    int cmsg_type;             /* SCM_RIGHTS */
    int cmsg_data[0];          /* file descriptor */
};
```

For parsing the file descriptor passed with SCM_RIGHTS, the runtime will `recvmsg` the message and only care about the control message containing ancillary data. It will read one byte from the `msg_iov` buffer just to receive the ancillary data, but it will disregard the contents of the `msg_iov` buffers.

### User_events format

#### User_events Registration

Once the runtime has received the configured tracepoint names as detailed under [tracepoint_config](#user_events-session-payload), it uses the [file descriptor passed in the continuation stream](#passing_file_descriptor) to register the prescribed tracepoint names following the [user_events registering protocol](https://docs.kernel.org/trace/user_events.html#registering). The runtime will construct a `user_reg` struct for every tracepoint name, defaulting to using none of the `user_reg` flags, so the resulting command format will be as follows:

##### Tracepoint Format V1

`<tracepoint_name> u8 version; u16 event_id; __rel_loc u8[] extension; __rel_loc u8[] payload`

See [user_events writing](#user_events-writing) below for field details`.

#### User_events Writing

When writing events to their mapped user_events tracepoints prescribed by the `tracepoint_config` in the [User_events session payload](#user_events-session-payload), the runtime will adapt the [user_events writing protocol](https://docs.kernel.org/trace/user_events.html#writing-data) to write the event as:

```
struct iovec io[7];

io[0].iov_base = &write_index;       // __u32 tracepoint write index from registration
io[0].iov_len = sizeof(write_index);
io[1].iov_base = &version;           // __u8 tracepoint format version
io[1].iov_len = sizeof(version);
io[2].iov_base = &event_id;          // __u16 EventID defined by EventSource/native manifest
io[2].iov_len = sizeof(event_id);
io[3].iov_base = &extension;         // __rel_loc u8[] optional event information
io[3].iov_base = sizeof(extension);
io[4].iov_base = &payload;           // __rel_loc u8[] event payload
io[4].iov_len = sizeof(payload);
io[6].iov_base = &data;              // __u8[] data
io[6].iov_len = data_len;

writev(ep_session->data_fd, (const struct iovec *)io, 7);
```

The `__rel_loc` is the relative dynamic array attribute described [here](https://lwn.net/Articles/876682/).

The `write_index` is the tracepoint's write index determined during tracepoint registration.

The `version` is the version of the tracepoint format, which in this case is [version 1](#tracepoint-format-v1).

The `event_id` is the ID of the event, defined by the EventSource/native manifest.

#### Extension Blob Format

The `extension` field is an optional data blob that can provide additional information about an event. Its structure is as follows:

1. **Label** (`byte`): Indicates the type of data that follows.
2. **Data**: The content, whose format depends on the label.

**Label Values and Corresponding Data:**

| Label  | Meaning                | Data Format                | Description                                                                 |
|--------|------------------------|----------------------------|-----------------------------------------------------------------------------|
| 0x01   | Event Metadata         | `array<byte> metadata`     | Contains event metadata, formatted per NetTrace v5.                         |
| 0x02   | ActivityId             | `uint16 guid`              | Contains the GUID for the ActivityId.                                       |
| 0x03   | RelatedActivityId      | `uint16 guid`              | Contains the GUID for the RelatedActivityId.                                |

**Details:**
- The extension blob may be empty if no extra information is present.
- Multiple extension blobs can be concatenated if more than one piece of information is needed. Each blob starts with its own label byte.
- For Event Metadata (`0x01`), the `metadata` array matches the NetTrace v5 PayloadBytes format.
- For ActivityId and RelatedActivityId (`0x02`, `0x03`), the `guid` is a 16-byte value representing the GUID.
- The size of the entire extension blob can be inferred from the extension `__rel_loc` field. See the [__rel_loc documentation](https://lwn.net/Articles/876682/) for more details.

**Example Layout:**

```
[Label][Data][Label][Data]...
```

For example, an extension blob containing both Event Metadata and ActivityId would look like:
- `[0x01][metadata][0x02][guid]`

**Notes:**
- The runtime includes Event Metadata only the first time an event is sent in a session.
- Native runtime events do not include metadata.

The `payload` points at a blob of data with the same format as an EventPipe payload – the concatenated encoded values for all the parameters.

The `metadata` either points at nothing if the event doesn’t have metadata, or it points at a metadata blob matching the NetTrace version 5 formatting convention. Specifically it is the data that would be stored inside the PayloadBytes area of an event blob within a MetadataBlock described [here](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/EventPipe/NetTraceFormat_v5.md#metadata-event-encoding).

> NOTE: V5 and V6 metadata formats have the same info, but they aren’t formatted identically. Parsing and reserialization is required to convert between the two.

### Which events have metadata?

The runtime will keep track per-session whether it has sent a particular event before. The first time each event is sent during a session, metadata will be included, and otherwise, it will be left empty. As a special case, runtime events currently implemented in native code will never send metadata.

## Dump Commands

### `CreateCoreDump`

Command Code: `0x0101`

The `CreateCoreDump` command is used to instruct the runtime to generate a core dump of the process.  The command will keep the connection open while the dump is generated and then respond with a message containing an `HRESULT` indicating success or failure.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0101; 0x0000 }`

* `string dumpName`: The name of the dump generated.
* `uint dumpType`: A value between 1 and 4 inclusive that indicates the type of dump to take
  * Normal = 1,
  * WithHeap = 2,
  * Triage = 3,
  * Full = 4
* `uint diagnostics`: If set to 1, log to console the dump generation diagnostics
  * `0` or `1` for on or off

#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`CreateCoreDump` returns:
* `int32 hresult`: The result of creating the core dump (`0` indicates success)

##### Details:

Input:
```
Payload
{
    string dumpName,
    uint dumpType,
    uint diagnostics
}
```

Returns:
```c
Payload
{
    int32 hresult
}
```

## Profiler Commands

### `AttachProfiler`

Command Code: `0x0301`

The `AttachProfiler` command is used to attach a profiler to the runtime.  The command will keep the connection open while the profiler is being attached and then respond with a message containing an `HRESULT` indicating success or failure.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0301; 0x0000 }`

* `uint32 attachTimeout`: The timeout for attaching to the profiler (in milliseconds)
* `CLSID profilerGuid`: The GUID associated with the profiler
* `string profilerPath`: Location of the profiler
* `array<byte> clientData`: The data being provided to the profiler

Where a `CLSID` is a fixed size struct consisting of:
* `uint x`
* `byte s1`
* `byte s2`
* `byte[8] c`

#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`AttachProfiler` returns:
* `int32 hresult`: The result of attaching the profiler (`0` indicates success)

##### Details:

Input:
```
Payload
{
    uint32 dwAttachTimeout
    CLSID profilerGuid
    string profilerPath
    uint32 clientDataSize
    array<byte> pClientData
}
```

Returns:
```c
Payload
{
    int32 hresult
}
```

## Process Commands

> Available since .NET 5.0

### `ProcessInfo`

Command Code: `0x0400`

The `ProcessInfo` command queries the runtime for some basic information about the process.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0400; 0x0000 }`

There is no payload.

#### Returns (as an IPC Message Payload):

Header: `{ Magic; size; 0xFF00; 0x0000; }`

Payload:
* `int64 processId`: the process id in the process's PID-space
* `GUID runtimeCookie`: a 128-bit GUID that should be unique across PID-spaces
* `string commandLine`: the command line that invoked the process
  * Windows: will be the same as the output of `GetCommandLineW`
  * Non-Windows: will be the fully qualified path of the executable in `argv[0]` followed by all arguments as the appear in `argv` separated by spaces, i.e., `/full/path/to/argv[0] argv[1] argv[2] ...`
* `string OS`: the operating system that the process is running on
  * macOS => `"macOS"`
  * Windows => `"Windows"`
  * Linux => `"Linux"`
  * other => `"Unknown"`
* `string arch`: the architecture of the process
  * 32-bit => `"x86"`
  * 64-bit => `"x64"`
  * ARM32 => `"arm32"`
  * ARM64 => `"arm64"`
  * Other => `"Unknown"`

##### Details:

Returns:
```c++
struct Payload
{
    uint64_t ProcessId;
    LPCWSTR CommandLine;
    LPCWSTR OS;
    LPCWSTR Arch;
    GUID RuntimeCookie;
}
```

### `ResumeRuntime`

Command Code: `0x0401`

If the target .NET application has been configured Diagnostic Ports configured to suspend with `DOTNET_DiagnosticPorts` or `DOTNET_DefaultDiagnosticPortSuspend` has been set to `1` (`0` is the default value), then the runtime will pause during `EEStartupHelper` in `ceemain.cpp` and wait for an event to be set.  (See [Diagnostic Ports](#diagnostic-ports) for more details)

The `ResumeRuntime` command sets the necessary event to resume runtime startup.  If the .NET application _has not_ been configured to with Diagnostics Monitor Address or the runtime has _already_ been resumed, this command is a no-op.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0401; 0x0000 }`

There is no payload.

#### Returns (as an IPC Message Payload):

Header: `{ Magic; size; 0xFF00; 0x0000; }`

There is no payload.

### `ProcessEnvironment`

Command Code: `0x0402`

The `ProcessEnvironment` command queries the runtime for its environment block.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0402; 0x0000 }`

There is no payload.

#### Returns (as an IPC Message Payload + continuation):

Header: `{ Magic; size; 0xFF00; 0x0000; }`

Payload:
* `uint32_t nIncomingBytes`: the number of bytes to expect in the continuation stream
* `uint16_t future`: unused

Continuation:
* `Array<Array<WCHAR>> environmentBlock`: The environment block written as a length prefixed array of length prefixed arrays of `WCHAR`.

Note: it is valid for `nIncomingBytes` to be `4` and the continuation to simply contain the value `0`.

##### Details:

Returns:
```c++
struct Payload
{
    uint32_t nIncomingBytes;
    uint16_t future;
}
```

> Available since .NET 6.0

### `ProcessInfo2`

Command Code: `0x0404`

The `ProcessInfo2` command queries the runtime for some basic information about the process. The returned payload has the same information as that of the `ProcessInfo` command in addition to the managed entrypoint assembly name and CLR product version.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0404; 0x0000 }`

There is no payload.

#### Returns (as an IPC Message Payload):

Header: `{ Magic; size; 0xFF00; 0x0000; }`

Payload:
* `int64 processId`: the process id in the process's PID-space
* `GUID runtimeCookie`: a 128-bit GUID that should be unique across PID-spaces
* `string commandLine`: the command line that invoked the process
  * Windows: will be the same as the output of `GetCommandLineW`
  * Non-Windows: will be the fully qualified path of the executable in `argv[0]` followed by all arguments as the appear in `argv` separated by spaces, i.e., `/full/path/to/argv[0] argv[1] argv[2] ...`
* `string OS`: the operating system that the process is running on
  * macOS => `"macOS"`
  * Windows => `"Windows"`
  * Linux => `"Linux"`
  * other => `"Unknown"`
* `string arch`: the architecture of the process
  * 32-bit => `"x86"`
  * 64-bit => `"x64"`
  * ARM32 => `"arm32"`
  * ARM64 => `"arm64"`
  * Other => `"Unknown"`
* `string managedEntrypointAssemblyName`: the assembly name from the assembly identity of the entrypoint assembly of the process. This is the same value that is returned from executing `System.Reflection.Assembly.GetEntryAssembly().GetName().Name` in the target process.
* `string clrProductVersion`: the product version of the CLR of the process; may contain prerelease label information e.g. `6.0.0-preview.6.#####`

##### Details:

Returns:
```c++
struct Payload
{
    uint64_t ProcessId;
    LPCWSTR CommandLine;
    LPCWSTR OS;
    LPCWSTR Arch;
    GUID RuntimeCookie;
    LPCWSTR ManagedEntrypointAssemblyName;
    LPCWSTR ClrProductVersion;
}
```

> Available since .NET 7.0

### `EnablePerfMap`

Command Code: `0x0405`

The `EnablePerfMap` command instructs the runtime to start emitting perfmap or jitdump files for the process. These files are used by the perf tool to correlate jitted code addresses in a trace.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0405; 0x0000 }`

Payload:
* `uint32_t perfMapType`: the type of generation to enable

#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`EnablePerfMap` returns:
* `int32 hresult`: The result of enabling the perfmap or jitdump files (`0` indicates success)

##### Details:

Inputs:
```c++
enum class PerfMapType
{
    DISABLED = 0,
    ALL      = 1,
    JITDUMP  = 2,
    PERFMAP  = 3
}

struct Payload
{
    uint32_t perfMapType;
}
```

Returns:
```c
Payload
{
    int32 hresult
}
```

> Available since .NET 8.0

### `DisablePerfMap`

Command Code: `0x0406`

The `DisablePerfMap` command instructs the runtime to stop emitting perfmap or jitdump files for the process. These files are used by the perf tool to correlate jitted code addresses in a trace.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0405; 0x0000 }`

Payload: There is no payload with this command.

#### Returns (as an IPC Message Payload):

Header: `{ Magic; 28; 0xFF00; 0x0000; }`

`DisablePerfMap` returns:
* `int32 hresult`: The result of enabling the perfmap or jitdump files (`0` indicates success)

##### Details:

Returns:
```c
Payload
{
    int32 hresult
}
```

> Available since .NET 8.0

### `ApplyStartupHook`

Command Code: `0x0407`

The `ApplyStartupHook` command is used to provide a path to a managed assembly with a [startup hook](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md) to the runtime. During diagnostic suspension, the startup hook path will be added list of hooks that the runtime will execute once it has been resumed.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0407; 0x0000 }`

* `string startupHookPath`: The path to the managed assembly that contains the startup hook implementation.

#### Returns (as an IPC Message Payload):

Header: `{ Magic; size; 0xFF00; 0x0000; }`

`ApplyStartupHook` returns:
* `int32 hresult`: The result of adding the startup hook (`0` indicates success)

##### Details:

Input:
```
Payload
{
    string startupHookPath
}
```

Returns:
```c++
struct Payload
{
    int32 hresult
}
```

> Available since .NET 8.0

### `ProcessInfo3`

Command Code: `0x0408`

The `ProcessInfo3` command queries the runtime for some basic information about the process. The returned payload is versioned and fields will be added over time.

In the event of an [error](#Errors), the runtime will attempt to send an error message and subsequently close the connection.

#### Inputs:

Header: `{ Magic; Size; 0x0408; 0x0000 }`

There is no payload.

#### Returns (as an IPC Message Payload):

Header: `{ Magic; size; 0xFF00; 0x0000; }`

Payload:
* `uint32 version`: the version of the payload returned. Future versions can add new fields after the end of the current structure, but will never remove or change any field that has already been defined.
* `uint64 processId`: the process id in the process's PID-space
* `GUID runtimeCookie`: a 128-bit GUID that should be unique across PID-spaces
* `string commandLine`: the command line that invoked the process
  * Windows: will be the same as the output of `GetCommandLineW`
  * Non-Windows: will be the fully qualified path of the executable in `argv[0]` followed by all arguments as the appear in `argv` separated by spaces, i.e., `/full/path/to/argv[0] argv[1] argv[2] ...`
* `string OS`: the operating system that the process is running on
  * macOS => `"macOS"`
  * Windows => `"Windows"`
  * Linux => `"Linux"`
  * other => `"Unknown"`
* `string arch`: the architecture of the process
  * 32-bit => `"x86"`
  * 64-bit => `"x64"`
  * ARM32 => `"arm32"`
  * ARM64 => `"arm64"`
  * Other => `"Unknown"`
* `string managedEntrypointAssemblyName`: the assembly name from the assembly identity of the entrypoint assembly of the process. This is the same value that is returned from executing `System.Reflection.Assembly.GetEntryAssembly().GetName().Name` in the target process.
* `string clrProductVersion`: the product version of the CLR of the process; may contain prerelease label information e.g. `6.0.0-preview.6.#####`
* `string runtimeIdentifier`: information to identify the platform this runtime targets, e.g. `linux_musl_arm`64, `linux_x64`, or `windows_x64` are all valid identifiers. See [.NET RID Catalog](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog) for more information.

##### Details:

Returns:
```c++
struct Payload
{
    uint32_t Version;
    uint64_t ProcessId;
    LPCWSTR CommandLine;
    LPCWSTR OS;
    LPCWSTR Arch;
    GUID RuntimeCookie;
    LPCWSTR ManagedEntrypointAssemblyName;
    LPCWSTR ClrProductVersion;
    LPCWSTR RuntimeIdentifier;
}
```

> Available since .NET 8.0

## Errors

In the event an error occurs in the handling of an Ipc Message, the Diagnostic Server will attempt to send an Ipc Message encoding the error and subsequently close the connection.  The connection will be closed **regardless** of the success of sending the error message.  The Client is expected to be resilient in the event of a connection being abruptly closed.

Errors are `HRESULTS` encoded as `int32_t` when sent back to the client.  There are a few Diagnostics IPC specific `HRESULT`s:
```c
#define CORDIAGIPC_E_BAD_ENCODING    = 0x80131384
#define CORDIAGIPC_E_UNKNOWN_COMMAND = 0x80131385
#define CORDIAGIPC_E_UNKNOWN_MAGIC   = 0x80131386
#define CORDIAGIPC_E_UNKNOWN_ERROR   = 0x80131387
```

Diagnostic Server errors are sent as a Diagnostic IPC Message with:
* a `command_set` of `0xFF`
* a `command_id` of `0xFF`
* a Payload consisting of a `int32_t` representing the error encountered (described above)

All errors will result in the Server closing the connection.

Error response Messages will be sent when:
* the client sends an improperly encoded Diagnostic IPC Message
* the client uses an unknown `command`
* the client uses an unknown `magic` version string
* the server encounters an unrecoverable error, e.g., OOM, transport error, runtime malfunction etc.

The client is expected to be resilient in the event that the Diagnostic Server fails to respond in a reasonable amount of time (this may be Command specific).

For example, if the Diagnostic Server finds incorrectly encoded data while parsing a Message, it would send the following Message in response:

<table>
  <tr>
    <th>1</th>
    <th>2</th>
    <th>3</th>
    <th>4</th>
    <th>5</th>
    <th>6</th>
    <th>7</th>
    <th>8</th>
    <th>9</th>
    <th>10</th>
    <th>11</th>
    <th>12</th>
    <th>13</th>
    <th>14</th>
    <th>15</th>
    <th>16</th>
    <th>17</th>
    <th>18</th>
    <th>19</th>
    <th>20</th>
    <th>21</th>
    <th>22</th>
    <th>23</th>
    <th>24</th>
    <th>25</th>
    <th>26</th>
    <th>27</th>
    <th>28</th>
  </tr>
  <tr>
    <td colspan="20">Header</td>
    <td colspan="8">Payload</td>
  </tr>
  <tr>
    <td colspan="14">magic</td>
    <td colspan="2">size</td>
    <td colspan="1">command_set</td>
    <td colspan="1">command_id</td>
    <td colspan="2">reserved</td>
    <td colspan="8">Error Code</td>
  </tr>
  <tr>
    <td colspan="14">"DOTNET_IPC_V1"</td>
    <td colspan="2">28</td>
    <td colspan="1">0xFF</td>
    <td colspan="1">0xFF</td>
    <td colspan="2">0x0000</td>
    <td colspan="8">0x80131384</td>
  </tr>
</table>

# Diagnostic Ports

> Available since .NET 5.0

A Diagnostic Port is a mechanism for communicating the Diagnostics IPC Protocol to a .NET application from out of process.  There are two flavors of Diagnostic Port: `connect` and `listen`.  A `listen` Port is when the runtime creates an IPC transport and listens for incoming connections.  The default Diagnostic Port is an example of a `listen` Port.  You cannot currently configure additional `listen` Ports.  A `connect` Port is when the runtime attempts to connect to an IPC transport owned by another process.  Upon connection to a `connect` Port, the runtime will send an [Advertise](#advertise-protocol) message signalling that it is ready to accept Diagnostics IPC Protocol commands.  Each command consumes a connection, and the runtime will reconnect to the `connect` Port to wait for more commands.

.NET applications can configure Diagnostic Ports with the following environment variables:

 * `DOTNET_DiagnosticPorts=<port address>[,tag[...]][;<port address>[,tag[...]][...]]`

where:

* `<port address>` is a NamedPipe name without `\\.\pipe\` on Windows, and the full path to a Unix domain socket on other platforms
* `tag ::= <SUSPEND_MODE> | <PORT_TYPE>`
* `<SUSPEND_MODE> ::= suspend | nosuspend` (default value is suspend)`
* `<PORT_TYPE> ::= connect` (future types such as additional listen ports could be added to this list)

Example usage:

```shell
$ export DOTNET_DiagnosticPorts=$DOTNET_DiagnosticPorts;~/mydiagport.sock,nosuspend;
```

Any diagnostic ports specified in this configuration will be created in addition to the default port (`dotnet-diagnostic-<pid>-<epoch>`). The suspend mode of the default port is set via the new environment variable `DOTNET_DefaultDiagnosticPortSuspend` which defaults to `0` for `nosuspend`.

Each port configuration specifies whether it is a `suspend` or `nosuspend` port. Ports specifying `suspend` in their configuration will cause the runtime to pause early on in the startup path before most runtime subsystems have started. This allows any agent to receive a connection and properly setup before the application startup continues. Since multiple ports can individually request suspension, the `resume` command needs to be sent by each suspended port connection before the runtime resumes execution.

If a config specifies multiple tag values from a tag type, for example  `"<path>,nosuspend,suspend,suspend,"`, only the first one is respected.

The port address value is **required** for a port configuration. If a configuration doesn't specify an address and only specifies tags, then the first tag will be treated as the path. For example, the configuration `DOTNET_DiagnosticPorts=nosuspend,connect` would cause a port with the name `nosuspend` to be created, in the default `suspend` mode.

The runtime will make a best effort attempt to generate a port from a port configuration. A bad port configuration won't cause an error state, but could lead to consumed resources. For example it could cause the runtime to continuously poll for a connect port that will never exist.

When a Diagnostic Port is configured, the runtime will attempt to connect to the provided address in a retry loop while also listening on the traditional server. The retry loop has an initial timeout of 10ms with a falloff factor of 1.25x and a max timeout of 500 ms.  A successful connection will result in an infinite timeout.  The runtime is resilient to the remote end of the Diagnostic Port failing, e.g., closing, not `Accepting`, etc.

## Advertise Protocol

Upon successful connection, the runtime will send a fixed-size, 34 byte buffer containing the following information:

 * `char[8] magic`: (8 bytes) `"ADVR_V1\0"` (ASCII chars + null byte)
 * `GUID runtimeCookie`: (16 bytes) CLR Instance Cookie (little-endian)
 * `uint64_t processId`: (8 bytes) PID (little-endian)
 * `uint16_t future`: (2 bytes) unused for future-proofing

With the following layout:

<table>
  <tr>
    <th>1</th>
    <th>2</th>
    <th>3</th>
    <th>4</th>
    <th>5</th>
    <th>6</th>
    <th>7</th>
    <th>8</th>
    <th>9</th>
    <th>10</th>
    <th>11</th>
    <th>12</th>
    <th>13</th>
    <th>14</th>
    <th>15</th>
    <th>16</th>
    <th>17</th>
    <th>18</th>
    <th>19</th>
    <th>20</th>
    <th>21</th>
    <th>22</th>
    <th>23</th>
    <th>24</th>
    <th>25</th>
    <th>26</th>
    <th>27</th>
    <th>28</th>
    <th>29</th>
    <th>30</th>
    <th>31</th>
    <th>32</th>
    <th>33</th>
    <th>34</th>
  </tr>
  <tr>
    <td colspan="8">magic</td>
    <td colspan="16">runtimeCookie</td>
    <td colspan="8">processId</td>
    <td colspan="2">future</td>
  </tr>
  <tr>
    <td colspan="8">"ADVR_V1\0"</td>
    <td colspan="16">123e4567-e89b-12d3-a456-426614174000</td>
    <td colspan="8">12345</td>
    <td colspan="2">0x0000</td>
  </tr>
</table>

This is a one-way transmission with no expectation of an ACK.  The tool owning the Diagnostic Port is expected to consume this message and then hold on to the now active connection until it chooses to send a Diagnostics IPC command.

## Dataflow

Due to the potential for an *optional continuation* in the Diagnostics IPC Protocol, each successful connection between the runtime and a Diagnostic Port is only usable **once**.  As a result, a .NET process will attempt to _reconnect_ to the diagnostic port immediately after every command that is sent across an active connection.

A typical dataflow has 2 actors, the Target application, `T` and the Diagnostics Monitor Application, `M`, and communicates like so:
```
T ->   : Target attempts to connect to M, which may not exist yet
// M comes into existence
T -> M : [ Advertise ] - Target sends advertise message to Monitor
// 0 or more time passes
T <- M : [ Diagnostics IPC Protocol ] - Monitor sends a Diagnostics IPC Protocol command
T -> M : [ Advertise ] - Target reconnects to Monitor with a _new_ connection and re-sends the advertise message
```

It is important to emphasize that a connection **_should not_** be reused for multiple Diagnostic IPC Protocol commands.
