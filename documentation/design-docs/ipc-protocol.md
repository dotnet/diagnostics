# Diagnostic IPC Protocol

## Overview

This spec describes a proposed IPC Protocol to be used for communicating with the runtime's diagnostics server from an external client over a platform-specific transport, e.g., Unix Domain Sockets. This protocol should be lightweight and generic, so as to be extensible for future use cases.  The primary use today is for EventPipe.

The IPC protocol will become a multi-purpose protocol to be used for communication to and from the diagnostics server running in the dotnet core runtime.  EventPipe is a good example of a service that will operate over this protocol.  This spec defines the structures and rules of communication at the protocol layer _only_, and does not dictate application specific communication.

### Terminology

The protocol will use the following names for various constructs and behaviors defined in this spec:
* *Diagnostic IPC Protocol*: The protocol defined in this spec
* *Diagnostic Server*: The server in the runtime that receives/sends Diagnostic IPC Procotol communication.
* *Application*: A service communicating over the Diagnostic IPC Protocol
* *Flow*: A sequence of interactions making up communication with the Diagnostics Server
* *Pipe*: The duplex communication construct this protocol is communicated over.  This is a Unix Domain Socket on *nix systems and a Named Pipe on Windows.
* *Header*: A struct containing version and metadata
* *Payload*: An opaque chunk of data that is application specific
* *IPC Message*: The base unit of communication over the Diagnostic IPC Protocol.  Is made up of a Header and a Payload.
* *Optional Continuation*: The reuse of the pipe for application specific communication.  This communication does not need to adhere to any requirements listed in this spec, e.g., this could be a stream of custom encoded data that is application specific.

### General Flow

![](ipc-protocol-genericflow.svg)

```
runtime <- client : [ header ][ optional payload ]
runtime -> client : [ header ][ optional payload ]
optional:
    // client to runtime stream
    runtime <- client : [ optional continuation ]
    ------or------
    // runtime to client stream
    runtime -> client : [ optional continuation ]
    ------or------
    // application specific reuse of duplex channel
    runtime -> client : [ optional continuation ]
    runtime <- client : [ optional continuation ]
connection closed
```

Example flow for EventPipe:
```
runtime <- client : [ ver; size; start_stream_command ][ stream config struct ]     <- IPC Message
runtime -> client : [ ver; size; stream_started_response ][ stream started struct ] <- IPC Message
runtime -> client : [ stream of netperf data ]                                      <- optional continuation

// stop message is sent on another connection

connection closed
```

Example flow for hypothetical application with custom communication protocol:
```
runtime <- client : [ ver; size; start_app_session_command ][ app config struct ]         <- IPC Message
runtime -> client : [ ver; size; app_session_started_response ][ session started struct ] <- IPC Message

Optional Continuation using app specific protocol

runtime <- client : [ app specific protocol ]                                             <- optional continuation
runtime -> client : [ app specific protocol ]                                             <- optional continuation
Eventually...
runtime <- client : [ app specific protocol (close connection) ]                          <- optional continuation
connection closed
```

### Transport

The protocol will be communicated over a platform-specific transport.  On Unix/Linux based platforms, a Unix Domain Socket will be used, and on Windows, a Named Pipe will be used.

#### Naming and Location Conventions

Unix Domain Sockets:
> TODO

Named Pipes:
> TODO

### Errors

The following list documents when errors may occur and what the expected response will be:
* Runtime side: 
  * read failure -> Error message & end connection
  * unknown protocol requested -> Error message & end connection
  * unknown version requested -> Error message & end connection
* Client side:
  * no response after initial client to runtime message -> client has a reasonable timeout and closes the connection if it expires

### Headers

Every Diagnostic IPC Message will start with a header and every header will:
* start with a magic version number and a size
* `sizeof(IpcHeader) == 20`
* encode numbers little-endian
* account for the size of the payload in the `size` value, i.e., `IpcHeader.size == sizeof(IpcHeader) + PayloadStruct.GetSize()`

> Taking inspiration from Mono's Soft-Debugger protocol, I am splitting command codes into command sets and commands, allowing command codes to be reused between command sets, e.g., 0x01,0x01 and 0x02,0x01 would be different commands despite sharing the same explicit value for the command code.

```c
// size = 14 + 2 + 2 + 2 = 20 bytes
struct IpcHeader
{
    // "DOTNET_IPC_V1" with trailing 0s
    char[14]   magic = { 0x68, 0x79, 0x84, 0x78, 0x69, 0x84, 0x95, 0x73, 0x80, 0x67, 0x95, 0x86, 0x49, 0x00 };
    uint16_t  size;     // size of packet = size of header + payload
    uint16_t  command;  // top byte is command_set & bottom byte is command_id
    uint16_t  reserved; // for potential future use
}
```

### Messages

#### Requirements:
* non-breaking - Wrapping current implementation in these packets shouldn't add more work than simply pulling the payload out, i.e., packet is FULLY opaque and orthogonal to the header.

Diagnostic IPC Messages will:
* contain a header and 0 or one payload
* have an opaque payload

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
    <td colspan="2">command</td>
    <td colspan="2">reserved</td>
    <td colspan="7">payload</td>
  </tr>
</table>

The simplest Diagnostic IPC Message will contain only a header and therefore only be 20 bytes long.

### Commands

Commands are a combination of a `command_set` (most significant byte) and a `command_id` (least significant byte).  Commands are application specific and don't affect the specification of this protocol beyond their encoding in the header.

As an example, the commands the Diagnostic Server are capable of consuming *_may_* look like this:

```c
enum command_sets : char
{
    // DEBUG = 0x00,
    EVENT_PIPE = 0x01,
    MISC = 0x02,
    // future
}
```

```c
enum event_pipe_commands : char
{
    START_STREAM_SESSION    = 0x00, // create/start a given session
    STOP_STREAM_SESSION     = 0x01, // stop a given session
}
```

In this example, the command to start a stream session would be `0x0100` and the command to stop streaming would be `0x0101`.

-----
### Current Implementation (OLD)

Single-purpose IPC potocol used exclusively for EventPipe functionality.  "Packets" in the current implementation are simply the `netperf` payloads and command/control is handled via `uint32` enum values sent one way with hardcoded responses expected.

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