# Diagnostics Client Library API Design 

The Diagnostics Client Library (currently named as "Runtime Client Library") is a managed library that can be used to interact with the .NET runtime via the diagnostics IPC protocol as documented in https://github.com/dotnet/diagnostics/blob/master/documentation/design-docs/ipc-protocol.md.


## Intro



### Name
The name "Diagnostics Client Library" comes from the fact that we call the runtime (CoreCLR) component responsible for accepting and handling the diagnostics IPC commands the "diagnostics server" - https://github.com/dotnet/coreclr/blob/master/src/vm/diagnosticserver.h. Since this library is a managed library on the other side of the IPC protocol responsible for communicating with the runtime's "diagnostics server", calling this the "Diagnostics Client Library" made sense.

## Goals

The goal of this library is as following:

* Serve as an implementation of the IPC protocol to communicate with CoreCLR's diagnostics server.
* Provide an easy-to-use API for any library/tools authors to utilize the IPC protocol 

## Non-Goals

* Provide tool-specific functionalities that are too high-level (i.e. dumping the GC heap, parsing counter payload, etc.)

## API Descriptions

* Classes:

```.cs

public DiagnosticsClient
{
    public static void EnumerateProcess()
}

```
