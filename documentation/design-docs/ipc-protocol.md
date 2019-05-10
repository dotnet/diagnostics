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
* *Header*: A struct containing version and metadata.
* *Payload*: An opaque chunk of data that is Command specific.
* *Optional Continuation*: The reuse of the pipe for application specific communication. This communication does not need to adhere to any requirements listed in this spec, e.g., this could be a stream of custom encoded data that is Command specific.

### General Flow

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
runtime <- client : [ ver; size; EventPipe CollectTracing ][ stream config struct  ] <- Diagnostic IPC Message
runtime -> client : [ ver; size; EventPipe TracingStarted ][ stream started struct ] <- Diagnostic IPC Message
runtime -> client : [ stream of netperf data ]                                       <- Optional Continuation

// stop message is sent on another connection

connection closed
```

Example flow for hypothetical application with custom communication protocol:
```
runtime <- client : [ ver; size; start_app_session_command    ][ app config struct      ] <- Diagnostic IPC Message
runtime -> client : [ ver; size; app_session_started_response ][ session started struct ] <- Diagnostic IPC Message

Optional Continuation using app specific protocol

runtime <- client : [ command specific protocol ]                                         <- Optional Continuation
runtime -> client : [ command specific protocol ]                                         <- Optional Continuation
Eventually...
runtime <- client : [ command specific protocol (close connection) ]                      <- Optional Continuation
connection closed
```

### Transport

The protocol will be communicated over a platform-specific transport.  On Unix/Linux based platforms, a Unix Domain Socket will be used, and on Windows, a Named Pipe will be used.

#### Naming and Location Conventions

Unix Domain Sockets:
> The socket is created in the `tmp` dir.  This will be determined by the output of the `Path.GetTempPath()` function in managed code.  On Mac, this is typically an application group specific temp directory which can be found in the `$TMPDIR` environment variable.

MacOS:
```
/$TMPDIR/dotnetcore-diagnostic-<PID>-<AppGroupID>-socket
```

Linux:
```
/tmp/dotnetcore-diagnostic-<PID>-<EpochTimestamp>-socket
```

Named Pipes (Windows):
```
\\.\pipe\dotnetcore-diagnostic-<PID>
```

### Messages

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

For example, this IPC Message sends a hypothetical Command (discussed later) `0xFFEE` which has an empty Payload:
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
    <td colspan="1">0xEE</td>
    <td colspan="2">0x0000</td>
  </tr>
</table>

#### Headers

Every Diagnostic IPC Message will start with a header and every header will:
* start with a magic version number and a size
* `sizeof(IpcHeader) == 20`
* encode numbers little-endian
* account for the size of the payload in the `size` value, i.e., `IpcHeader.size == sizeof(IpcHeader) + PayloadStruct.GetSize()`

```c
// size = 14 + 2 + 1 + 1 + 2 = 20 bytes
struct IpcHeader
{
    char[14]  magic;    // null terminated string of the version of the protocol, for example "DOTNET_IPC_V1"
    uint16_t  size;     // size of packet = size of header + payload
    uint8_t   command_set;  // combined with command_id is the Command to invoke
    uint8_t   command_id;   // combined with command_set is the Command to invoke
    uint16_t  reserved; // for potential future use
};
```

The `reserved` field is reserved for future use.  It is unused in `DOTNET_IPC_V1`, so it should be set to `0x0000` by convention when unused.


#### Payloads

Payloads are Command specific data encoded into a Diagnostic IPC Message.  The size of the payload is implicitly encoded in the Header's `size` field as `PayloadSize = header.size - sizeof(struct IpcHeader)`.  A Payload _may_ be 0 bytes long if it empty.  The encoding of data in the Payload is Command specific.

As an example, EventPipe encodes non fixed-size Payloads using type codes, little-endian numbers, and size-prefixed, null-terminated char strings.  Using these rules, a hypothetical Diagnostic IPC Message telling EventPipe to start streaming _may_ look like the following:

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
    <th>35</th>
    <th>36</th>
    <th>37</th>
    <th>38</th>
    <th>39</th>
    <th>40</th>
    <th>41</th>
    <th>42</th>
    <th>43</th>
    <th>44</th>
    <th>45</th>
    <th>46</th>
  </tr>
  <tr>
    <td colspan="20">Header</td>
    <td colspan="26">Payload</td>
  </tr>
  <tr>
    <td colspan="14">magic</td>
    <td colspan="2">size</td>
    <td colspan="2">command</td>
    <td colspan="2">reserved</td>
    <td colspan="1">type_code</td>
    <td colspan="8">PID</td>
    <td colspan="1">type_code</td>
    <td colspan="2">String Length</td>
    <td colspan="14">Providers String</td>
  </tr>
  <tr>
    <td colspan="14">"DOTNET_IPC_V1"</td>
    <td colspan="2">46</td>
    <td colspan="2">0x0202</td>
    <td colspan="2">0x0000</td>
    <td colspan="1">UINT64</td>
    <td colspan="8">1234</td>
    <td colspan="1">STRING</td>
    <td colspan="2">14</td>
    <td colspan="14">"MyEventSource"</td>
  </tr>
</table>

Where `0x0202` is the Command to start streaming with EventPipe.

### Commands

Commands are a `command_set` and a `command_id`.  A `command_set` is analogous to a namespace for `command_id`s.  The typical grouping is by service running on the Diagnostic Server, e.g., there is a `command_set` for EventPipe.  This allows multiple services to have the same `command_id`s without clashing.  The combination of a `command_set` and a `command_id` encodes the Command being invoked on the Diagnostic Server.

The current set of `command_set`s and `command_id`s are listed below:

```c
enum class CommandSet : uint8_t
{
    // reserved = 0x00,
    Miscellaneous = 0x01,
    EventPipe     = 0x02,
    // future

    Server = 0xFF,
};
```

```c
enum class ServerCommandId : uint8_t
{
    OK    = 0x00,
    Error = 0xFF,
};
```

```c
enum class EventPipeCommandId : uint8_t
{
    // reserved = 0x00,
    StopTracing    = 0x01, // create/start a given session
    CollectTracing = 0x02, // stop a given session
}
```

Commands may use the generic `{ magic="DOTNET_IPC_V1"; size=20; command_set=0xFF (Server); command_id=0x00 (OK); reserved = 0x0000; }` to indicate success rather than having a command specific success `command_id`.  Similarly, Commands may use the `command_set=0xFF (Server); command_id=0xFF (Error);` to generically indicate an error has occurred.

For example, the Command to start a stream session with EventPipe would be `0x0202` made up of `0x02` (the `command_set` for EventPipe) and `0x02` (the `command_id` for CollectTracing).

### Errors

Errors are mostly Command specific, but there are some generic Diagnostic Server errors described in the code snippet below.

```c++
enum class DiagnosticServerErrorCode : uint32_t
{
    OK                = 0x00000000,
    BadEncoding       = 0x00000001,
    UnknownCommandSet = 0x00000002,
    UnknownCommandId  = 0x00000003,
    UnknownVersion    = 0x00000004,
    // future

    BAD               = 0xFFFFFFFF,
};
```

Diagnostic Server errors are sent as a Diagnostic IPC Message with:
* a `command_set` of `0xFF`
* a `command_id` of `0xFF`
* a Payload consisting of a `uint32_t` representing the error encountered (described above)

All errors will result in the Server closing the connection.

Error response Messages will be sent when:
* the client sends an improperly encoded Diagnostic IPC Message
* the client uses an unknown `command_set`
* the client uses an unknown `command_id`
* the client uses an unknown `magic` version string
* the server encounters an unrecoverable error

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
    <td colspan="8">0x00000001</td>
  </tr>
</table>

-----
### Current Implementation (OLD)

Single-purpose IPC protocol used exclusively for EventPipe functionality.  "Packets" in the current implementation are simply the `netperf` payloads and command/control is handled via `uint32` enum values sent one way with hard coded responses expected.

```c++
enum class DiagnosticMessageType : uint32_t
{
    // EventPipe
    StartEventPipeTracing = 1024, // To file
    StopEventPipeTracing,
    CollectEventPipeTracing, // To IPC
};

struct MessageHeader
{
    DiagnosticMessageType RequestType;
    uint32_t Pid;
};
```

```
runtime <- client : MessageHeader { CollectEventPipeTracing }
    error? -> 0 then session close
runtime -> client : session ID 
runtime -> client : event stream

...

runtime <- client : stop command
runtime -> client : session id and stops
```