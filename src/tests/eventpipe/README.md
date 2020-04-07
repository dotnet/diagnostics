# EventPipe testing in dotnet/diagnostics

This directory contains the dotnet/diagnostics end of testing for EventPipe and related infrastructure. Not all aspects of EventPipe are tested here. The table below indicates where specific parts of the feature are being tested, including the tests in this directory.

| completed | functionality    | location |
| --------- | ---------------- | -------- |
| ✅        | IPC protocol     | dotnet/runtime/src/coreclr |
| ✅        | EventPipe Provider Enable/Disable | dotnet/runtime/src/coreclr |
| ✅        | EventPipe Event-Provider coherence | dotnet/runtime/src/coreclr |
| ✅        | `dotnet trace` provider parsing | dotnet/diagnostics |
| ✅        | `dotnet trace` provider-profile merging | dotnet/diagnostics |

The tests here are meant to cover the diagnostic scenarios rather than correctness of the feature. They will transitively test that, but the main focus is whether typical scenarios work _using_ the technology.  In short, these test should answer the following questions:
* Does EventPipeEventSource + IPC Protocol + EventPipe collect the events we expect to see?
* Do typical diagnostic pairings of Issue + "Event to diagnose the issue" work via EventPipe?
  * e.g., if my app is starving for threads, can I turn on Thread events and successfully collect them.