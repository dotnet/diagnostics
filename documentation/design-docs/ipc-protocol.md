# IPC Protocol

## Overview

The IPC Protocol is a means of standardizing communication with the runtime via a duplex pipe/socket.  This protocol is meant to be generic enough that future feature additions in the runtime can make use of it.

## TODOs
* formalize struct names
* formalize error codes (Use HRESULT values?)

## Inter-Process Communication Specification

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
    error? -> no response
runtime -> client : session ID 
runtime -> client : event stream

...

runtime <- client : stop command
runtime -> client : session id and stops
```
-----

## Proposed Implementation (NEW)

The IPC protocol will become a multi-purpose protocol to be used for communication to and from command servers running in the dotnet core runtime.  EventPipe is a good example of a service that will operate over this protocol.

Connection (general):

![](ipc-protocol-genericflow.svg)

```
runtime <- client : [ header ][ optional payload ]
runtime -> client : [ header ][ optional payload ]
optional
    runtime <- client : [ optional stream of command specific data ]
    ------or------
    runtime -> client : [ optional stream of command specific data ]
```

EventPipe flow:
```
runtime <- client : [ ver; size; start_stream_command ][ stream config struct ]
runtime -> client : [ ver; size; stream_started_response ][ stream started struct ]
runtime -> client : [ stream of netperf data ]
```

### Transport considerations

* All data will be sent little-endian
* All commands will be sent in a packet with the defined header
* Command specific data can be sent after a command ACK


### Errors
* runtime: 
  * read failure -> end connection; no response
  * unknown protocol -> ERROR response
* Client:
  * no response -> simply have a timeout
  * unkown protocol error -> fix your client

### Headers

#### Requirements:
* small - shouldn't affect write times or read times
* simple - flat and blittable
* descriptive - self describing
* extensible - ability for us to add functionality without necessarily incrementing protocol version

Every packet will start with a header and every header will start with a version magic number and a size.  This makes it easy to read the data directly out of the stream no matter the size or contents of the packet.

> Taking inspiration from Mono's Soft-Debugger protocol, I am splitting command codes into command sets and commands, allowing command codes to be reused between command sets, e.g., 0x01,0x01 and 0x02,0x01 would be different commands despite sharing the same explicit value for the command code.

```c
// size = 16 + 2 + 2 + 2 = 22 bytes
struct header
{
    // "DOTNET_IPC_V1" with trailing 0s
    char[16]   magic = { 0x68, 0x79, 0x84, 0x78, 0x69, 0x84, 0x95, 0x73, 0x80, 0x67, 0x95, 0x86, 0x49, 0x00, 0x00, 0x00 };
    uint16_t  size; // size of packet = size of header + payload
    uint16_t  command; // top two bytes are command_set & bottom two bytes are command_id
    uint16_t  reserved; // for potential future use
}
```

### Packets

#### Requirements:
* non-breaking - Wrapping current implementation in these packets shouldn't add more work than simply pulling the payload out, i.e., packet is FULLY opaque and orthogonal to the header.

Packets will contain a header and 0 or one payload.  The payload will completely opaque with the client and runtime agreeing on some format/encoding of the data.  The size will be known as it must be `packet.header.size - sizeof(header)`.

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
    <th>...</th>
    <th>size - 1 </th>
    <th>size</th>
  </tr>
  <tr>
    <td colspan="22">header</td>
    <td colspan="7">payload</td>
  </tr>
  <tr>
    <td colspan="16">magic</td>
    <td colspan="2">size</td>
    <td colspan="2">command</td>
    <td colspan="2">reserved</td>
    <td colspan="7">payload</td>
  </tr>
</table>

The simplest of command/reply packets will only contain a header and therefore be only 22 bytes long.

### Commands Sets


```c
enum command_sets : char
{
    EVENT_PIPE = 0x01,
    // future
}
```

### `EVENT_PIPE` Commands

#### Requirements:
* minimal
* as close to a no-op with current code as possible


```c
enum event_pipe_commands : char
{
    START_STREAM_SESSION    = 0x00, // create/start a given session
    STOP_STREAM_SESSION     = 0x01, // stop a given session
    UPDATE_STREAM_SESSION   = 0x02, // update a given session
    
}
```


```c
enum event_pipe_responses : char
{
    STREAM_SESSION_SUCCESS    = 0x00, // command success
    STREAM_SESSION_FAILURE    = 0x01, // command failure
}
```

A complete EventPipe command would look like this then:
```
0x0100 => start a new session
0x0101 => stop a session
```