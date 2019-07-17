# EventPipe Test Plan

We intend to test the following areas of `EventPipe`
- Number of events _sent_ versus number of events _received_
  - If I send `N` events I should receive `N +/- d` events, where `d` is some error bound.  This should work for:
    - [x] small buffer sizes (larger `d`) (see: [buffersize](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/buffersize))
    - [x] large buffer sizes (smaller `d`) (see: [providervalidation](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/providervalidation))
    - [x] various providers (see: [providervalidation](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/providervalidation))
- Providers _enabled_ versus providers _observed_
  - If I enable provider `A`, I should see events from provider `A` in the stream.  This should work for:
    - [x] small buffer sizes (see: [buffersize](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/buffersize))
    - [x] large buffer sizes (see: [providervalidation](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/providervalidation))
    - [x] custom `EventSource`s (see: [providervalidation](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/providervalidation))
    - [x] baked in providers, e.g., `Microsoft-Windows-DotNETRuntime` (see: [providervalidation](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/providervalidation))
- `EventPipe` and `EventListener` sessions should be _coherent_
  - If I run an `EventListener` with the _same_ providers as an `EventPipe` session at the _same_ time, I should see the same:
    - [] event counts
    - [] providers (with the exception of rundown events only being in `EventPipe`)
- `EventPipe` should work with multiple sessions
  - If I run create `N` `EventPipe` sessions, I should see:
    - [] a session contains events _only_ from providers _that_ session enabled
    - [] sessions receives the _maximum_ verbosity configured for all sessions for a given provider, .e.g., if provider `A` is enabled by session `0x01` at verbosity `2` and then session `0x02` enables provider `A` at verbosity `3`, then both sessions will see events with verbosity `3`
    - [] additional sessions can be started or stopped without affecting existing sessions
- _"Special"_ providers operate correctly
  - [] There should be stacks in the output trace if the `Microsoft-DotNETCore-SampleProfiler` provider is enabled
  - [x] The `Microsoft-DotNETCore-Rundown` provider should _almost_ always be present (see: [rundownvalidation](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/rundownvalidation))
  - [x] Rundown includes `MethodDCEnd` and `MethodILToNativeMap` events (see: [rundownvalidation](https://github.com/dotnet/coreclr/tree/master/tests/src/tracing/eventpipe/rundownvalidation))

## Test Design
The test infrastructure goes through the following phases, with the ability for test writers to intervene at specific steps.

1. Configure an `EventPipe` session (test writers can provide their own)
2. Inject `SentinelEventSource` into provider list
3. Create `EventPipe` session
4. create `EventPipeEventSource` and wire up the `Dynamic.All` parser event to count the number of events for each provider
5. (Optional) Run additional test code over `EventpipeEventSource` (see: [`_DoesRundownContainMethodEvents` in rundownvalidation](https://github.com/dotnet/coreclr/blob/master/tests/src/tracing/eventpipe/rundownvalidation/rundownvalidation.cs#L45-L53))
6. Listen for `SentinelEventSource` event to begin test
7. Run provided `EventGeneratingAction`
8. Close session
9. Compare observed events with expected events
