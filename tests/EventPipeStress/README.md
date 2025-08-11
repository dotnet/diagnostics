# EventPipe Stress

You can use `Orchestrator` and `Stress` to run several stress test scenarios for EventPipe.

These tools are meant for developers working on EventPipe in dotnet/runtime.

## Orchestrator

Help text:

```
$ Orchestrator -h
Orchestrator:
  EventPipe Stress Tester - Orchestrator

Usage:
  Orchestrator [options] <stress-path>

Arguments:
  <stress-path>    The location of the Stress executable.

Options:
  --event-size <event-size>                       The size of the event payload. The payload is a string, so the actual size will be eventSize * sizeof(char) where sizeof(char) is 2 Bytes due to Unicode in C#. [default: 100]
  --event-rate <event-rate>                       The rate of events in events/sec. -1 means 'as fast as possible'. [default: -1]
  --burst-pattern <BOLUS|DRIP|HEAVY_DRIP|NONE>    The burst pattern to send events in. [default: NONE]
  --reader-type <EventPipeEventSource|Stream>     The method to read the stream of events. [default: Stream]
  --slow-reader <slow-reader>                     <Only valid for EventPipeEventSource reader> Delay every read by this many milliseconds. [default: 0]
  --duration <duration>                           The number of seconds to send events for. [default: 60]
  --cores <cores>                                 The number of logical cores to restrict the writing process to. [default: 8]
  --threads <threads>                             The number of threads writing events. [default: 1]
  --event-count <event-count>                     The total number of events to write per thread. -1 means no limit [default: -1]
  --rundown                                       Should the EventPipe session request rundown events? [default: True]
  --buffer-size <buffer-size>                     The size of the buffer requested in the EventPipe session [default: 256]
  --iterations <iterations>                       The number of times to run the test. [default: 1]
  --pause                                         Should the orchestrator pause before starting each test phase for a debugger to attach? [default: False]
  --version                                       Show version information
  -?, -h, --help                                  Show help and usage information
```

## Stress

Help text:

```
$ Stress -h
Stress:
  EventPipe Stress Tester - Stress

Usage:
  Stress [options]

Options:
  --event-size <event-size>                       The size of the event payload. The payload is a string, so the actual size will be eventSize * sizeof(char) where sizeof(char) is 2 Bytes due to Unicode in C#. [default: 100]
  --event-rate <event-rate>                       The rate of events in events/sec. -1 means 'as fast as possible'. [default: -1]
  --burst-pattern <BOLUS|DRIP|HEAVY_DRIP|NONE>    The burst pattern to send events in. [default: NONE]
  --duration <duration>                           The number of seconds to send events for. [default: 60]
  --threads <threads>                             The number of threads writing events. [default: 1]
  --event-count <event-count>                     The total number of events to write per thread. -1 means no limit [default: -1]
  --version                                       Show version information
  -?, -h, --help                                  Show help and usage information
```

## Usage

### Prerequisites

1. Build `Orchestrator`
2. Publish `Stress` as a self contained application (`dotnet publish -r <RID> --self-contained`)
3. (optional) Copy over the runtime bits in the `Stress` publish location to test private runtime builds

### Basic Scenarios

1. Send as many events as possible in `N` seconds

`Orchestrate <path-to-Stress> --duration <N> --iterations 100`

2. Send `N` events as fast as possible

`Orchestrate <path-to-Stress> --event-count <N> --iterations 100`

3. Send `N` events in a burst pattern of `M` events/sec

`Orchestrate <path-to-Stress> --event-count <N> --event-rate <M> --burst-pattern bolus --iterations 100`

### Sample Output

```
**** Summary ****
iteration 1: 102,678.00 events collected, 0.00 events dropped in 0.283581 seconds - (100.00% throughput)
        (362,076.44 events/s) (181,038,221.88 bytes/s)
iteration 2: 102,678.00 events collected, 0.00 events dropped in 0.634398 seconds - (100.00% throughput)
        (161,851.05 events/s) (80,925,526.10 bytes/s)
iteration 3: 102,678.00 events collected, 0.00 events dropped in 0.652566 seconds - (100.00% throughput)
        (157,344.96 events/s) (78,672,477.98 bytes/s)
iteration 4: 102,678.00 events collected, 0.00 events dropped in 0.661910 seconds - (100.00% throughput)
        (155,123.83 events/s) (77,561,915.90 bytes/s)
iteration 5: 102,678.00 events collected, 0.00 events dropped in 0.632966 seconds - (100.00% throughput)
        (162,217.35 events/s) (81,108,673.20 bytes/s)


|-----------------------------------|--------------------|--------------------|--------------------|--------------------|
| stat                              | Min                | Max                | Average            | Standard Deviation |
|-----------------------------------|--------------------|--------------------|--------------------|--------------------|
| Events Read                       |          102,678.00|          102,678.00|          102,678.00|                0.00|
| Events Dropped                    |                0.00|                0.00|                0.00|                0.00|
| Throughput Efficiency (%)         |              100.00|              100.00|              100.00|                0.00|
| Event Throughput (events/sec)     |          155,123.83|          362,076.44|          199,722.73|           81,221.41|
| Data Throughput (Bytes/sec)       |       77,561,915.90|      181,038,221.88|       99,861,363.01|       40,610,702.78|
| Duration (seconds)                |            0.283581|            0.661910|            0.573084|            0.145165|
|-----------------------------------|--------------------|--------------------|--------------------|--------------------|
```