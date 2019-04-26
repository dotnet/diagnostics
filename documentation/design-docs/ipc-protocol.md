# Event Pipe

## Overview

> TODO: Write an overview of what this does

## TODOs
* formalize struct names
* formalize error codes (Use HRESULT values?)

## Inter-Process Communication Specification

### Current

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

### Proposed

Connection:
```
runtime <- client : handshake (set length byte array that represents version)
runtime -> client : sends handshake back as ACK
runtime <- client : packet containing start code
runtime -> client : ACK + potential payload
repeat till closed
```

### Transport considerations

* All data will be sent in the endianess of the machine
* All data will be sent in a packet containing a header of information concerning the contents

### Handshake

#### Requirements:
* simple - easy to code
* fast - no complex structures/serialization
* extensible - leaves room to change resulting behavior

The client will initiate the connection by sending a byte array containing the ASCII codes for the version string of the protocol it wishes to communicate with.

> There will only be one for now, but it will give us the ability to extend this and potentially have _other_ protocols we can speak if we'd like.

```c
// "EVENTPIPE_V1"
char[15] handshake = { 0x00, 0x00, 0x00, 0x45, 0x56, 0x45, 0x4e, 0x54, 0x50, 0x49, 0x50, 0x45, 0x5f, 0x56, 0x31 };
```

The `Accept` function in the runtime will immediately try to read 15 bytes of data and then use that determine what protocol we are speaking.  Once it has been determined, the runtime will respond with that handshake.

#### Errors
* runtime: 
  * read failure -> end connection; no response
  * unknown protocol -> ERROR response // TODO: flesh out errors
* Client:
  * no response -> simply have a timeout
  * unkown protocol error -> fix your client

### Headers

#### Requirements:
* small - shouldn't affect write times or read times
* simple - flat and blittable
* descriptive - self describing
* extensible - ability for us to add funcitonality without necessarily incrementing protocol version

Every packet will start with a header and every header will start with a size.  This makes it easy to read the data directly out of the stream no matter the size or contents of the packet.

> Taking inspiration from Mono's Soft-Debugger protocol, I am splitting command codes into command sets and commands, allowing command codes to be reused between command sets, e.g., 0x01,0x01 and 0x02,0x01 would be different commands despite sharing the same explicit value for the command code.

```c
// size = 4 + 1 + 1 + 1 = 7 bytes
struct header
{
    uint16 size; // size of packet = size of header + payload
    char   flag; // command or response
    char   command_set;
    char   command;
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
    <th>...</th>
    <th>size - 1 </th>
    <th>size</th>
  </tr>
  <tr>
    <td colspan="7">header</td>
    <td colspan="7">payload</td>
  </tr>
  <tr>
    <td colspan="4">size</td>
    <td>flag</td>
    <td>command_set</td>
    <td>command</td>
    <td colspan="7">payload</td>
  </tr>
</table>

The simplest of command/reply packets will only contain a header and therefore be only 7 bytes long.

### Commands Sets

#### Requirements:
// TODO

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
    CLOSE_STREAM_SESSION    = 0x00, // stop a given session
    START_STREAM_SESSION    = 0x01, // create/start a given session
    UPDATE_STREAM_SESSION   = 0x02, // update a given session
    GET_STREAM_SESSION      = 0x03, // get info on a given session
    PAUSE_STREAM_SESSION    = 0x04, // temporarily pause a given session
}
```

State machine diagram:
```
TODO
```

...