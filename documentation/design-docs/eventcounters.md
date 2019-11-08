# EventCounters

## Introduction
EventCounters are lightweight, cross-platform, and near real-time EventCounters are .NET Core APIs that were added to replace the "performance counters" on the .NET Framework days. EventCounters are designed to be lightweight way of measuring quick. This doc serves as a guide on what they are, how they are implemented, how to use them, and how to consume them. 

EventCounters were initially added in .NET Core 2.2 (*CHECK THIS*) but they have been changed and extended starting in .NET Core 3.0. In addition to that, the .NET Core runtime (CoreCLR) and few .NET libraries have started publishing basic diagnostics information using EventCounters starting in .NET Core 3.0. 

This document serves to explain some of the concepts and design choices behind EventCounters API, how to use them, and how they can be consumed both in-proc and out-of-proc with some sample code.


## Conceptual Overview
EventCounters collect numeric values over some time, and report them. The .NET Core runtime (CoreCLR) has several EventCounters, which collect and publish basic performance metrics related to CPU usage, memory usage, GC heap statistics, threads and locks statistics, etc. These serve as a basic performance guideline that can be easily consumed at a relatively cheap cost (that is, turning them on does not cause performance regression, so they can be used in production scenarios). 

Apart from the EventCounters that are already provided by the .NET runtime or the rest of the framework (i.e. ASP.NET, gRPC, etc.), you may choose to implement your own EventCounters to keep track of various metrics for your service. 

EventCounters live as a part of an [EventSource](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsource?view=netcore-3.0). Like any other events on an `EventSource`, they can be consumed both in-proc and out-of-proc via [EventListener](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventlistener?view=netcore-3.0) and EventPipe/ETW.

![EventCounter](EventCounters.jpg)

## EventCounters API Overview
At a high level, there are two types of counters in terms of their *purpose* - counters for ever-increasing values (i.e. Total # of exceptions, Total # of GCs, Total # of requests, etc.) and "snapshot" values (heap usage, CPU usage, working set size, etc.). Within each of these categories of counters, there are also two types of counters for implementation details - polling counters and non-polling counters. That gives us a total of 4 different counters, and each of these can be implemented by `EventCounter`,   `PollingCounter`, `IncrementingEventCounter`, and `IncrementingPollingCounter`. 

### EventCounter vs IncrementingEventCounter
`EventCounter` and `IncrementingEventCounter` capture metrics of different characteristics. `EventCounter` is meant to capture data that are more suitable as snapshot. For example, the total number of requests received for a web service can be captured by calling `EventCounter.WriteMetric(1)` every time a request is received. However, the value reported would look like:

```
"Name": "total-requests",
"Min": "1",
"Max": "1",
"Mean": "1",
"StandardDeviation": "0",
"Count": "25",
"CounterType": "Mean",
/* some more fields */
```

This is not very useful - we have to do an additional computation of multiplying the count by the mean to find out how many requests were received in during the second, not to mention the meaninglessness of "standard deviation", "min", and "max", since we are just logging "1" every time we receive a request. 

An `IncrementingEventCounter` is a type of EventCounter that is designed to capture such metrics that are ever-increasing. `IncrementingEventCounter` reports the total increment over the period of time it reports value. For example, if the value was incremented by 50 over the past second, it will report `50` as its payload, so that no additional computation is needed on the consumption side. The payload of an `IncrementingEventCounter` looks like:

```
"Name": "total-requests",
"DisplayName": "Total Requests",
"Increment": "25.0",
"CounterType": "Sum",
/* some more fields */
```

Most notable difference is that there is only `Increment`, and the statistical data such as `Min`, `Max`, `Mean`, and `StandardDeviation` no longer makes sense to be reported, so they don't exist. The `CounterType` is also reported as `Sum`, which is different from `EventCounter` payloads, which report it as `Mean`. 

Note that `IncrementingEventCounter` does not capture the total count over the lifetime of the process. (i.e. how many requests has been served since the process started). It only reports the *difference* over the period of the time it reports value. When monitoring performance, it is not very interesting to see the aggregate metric over the process' lifetime, since that does not provide any insights about the performance. For example, the total number of exceptions thrown since the process started does not indicate anything about the process' state. The actual interesting metric is the *rate* of the exception. (i.e. A process throwing 100 exceptions per second and has been running for 10 seconds is probably at a worse state than a process that throws 1 exception per hour and has been running for 5000 hours, even though the *total* exceptions is 1000 for the former and 5000 for the latter.)

### EventCounter vs PollingCounter
Both `EventCounter` and `PollingCounter` capture similar type of metrics. They are suitable for capturing "data at the moment". The main difference between `EventCounter` and `PollingCounter` is how the value is collected. When you write an `EventCounter`, you *push* the values to it at any point in time. On the contrary, `PollingCounter`s take the `pull` model, where it regularly polls a certain value at the interval it has to report values for. `PollingCounter` takes in a delegate as a parameter in its constructor, which it invokes once per the update interval specified. For example, if you pass in `GC.GetTotalMemory(false)` to its constructor, and subscribe to it by setting its update interval as once per second, it will call `GC.GetTotalMemory(false)` once every second and report that value. The payload reported by `PollingCounter` is identical to `EventCounter`. 

One advantage of using `PollingCounter` over `EventCounter` is that you don't have to explicitly call `WriteMetric` on it, providing more flexibility. A disadvantage may be that there is an additional state to maintain in the app that needs to be polled by `PollingCounter`.

Similar to how `PollingCounter` is a pull model for `EventCounter`, `IncrementingPollingCounter` is a pull model for `IncrementingEventCounter`.


## Writing EventCounters

Let's begin by looking at a couple of sample EventCounter implementation in the .NET Core runtime (CoreCLR). Here is the runtime implementation for the rate of exceptions thrown:

```cs
    IncrementingPollingCounter exceptionCounter = new IncrementingPollingCounter(
        "exception-count",
        this, 
        () => Exception.GetExceptionCount()
    ) 
    {
        DisplayName = "Exception Count",
        DisplayRateTimeScale = new TimeSpan(0, 0, 1)
    };
```

## Consuming EventCounters

There are two main ways of consuming EventCounters: in-proc and out-of-proc. 

### Consuming in-proc

You can consume the counter values via the `EventListener` API. `EventListener` is an in-proc way of consuming any Events written by all instances of EventSources in your application. For more details on how to use the EventListener API, visit https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventlistener.

First, the EventSource that produces the counter value needs to be enabled. To do this, you can override the `OnEventSourceCreated` method to get a notification when an EventSource is created, and if this is the correct EventSource with your EventCounters, then you can call Enable on it. Here is an example of such override:

```cs
protected override void OnEventSourceCreated(EventSource source)
{
    if (source.Name.Equals("System.Runtime"))
    {
        Dictionary<string, string> refreshInterval = new Dictionary<string, string>()
        {
            { "EventCounterIntervalSec", "1" }
        };
        EnableEvents(source, 1, 1, refreshInterval);
    }
}
```

### Sample Code

This is a sample `EventListener` class that simply prints out all the counter names and values from a the .NET runtime's EventSource for publishing its internal counters (`System.Runtime`) at some interval.

```cs
public class SimpleEventListener : EventListener
{        
    private readonly EventLevel _level = EventLevel.Verbose;

    public int EventCount { get; private set; } = 0;

    private int _intervalSec;

    public SimpleEventListener(int intervalSec)
    {
        _intervalSec = intervalSec;
    }


    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name.Equals("System.Runtime"))
        {
            Dictionary<string, string> refreshInterval = new Dictionary<string, string>() 
            {
                { "EventCounterIntervalSec", _intervalSec.ToString() }
            };
            EnableEvents(source, _level, (EventKeywords)(-1), refreshInterval);
        }
    }

    private (string Name, string Value) getRelevantMetric(IDictionary<string, object> eventPayload)
    {
        string counterName = "";
        string counterMean = "";
        string counterIncrement = "";
        bool isIncrement = false;

        foreach ( KeyValuePair<string, object> payload in eventPayload )
        {
            string key = payload.Key;
            string val = payload.Value.ToString();

            if (key.Equals("Name"))
            {
                counterName = val;
            }
            else if (key.Equals("Mean"))
            {
                counterMean = val;
            }
            else if (key.Equals("Increment"))
            {
                counterIncrement = val;
                isIncrement = true;
            }
        }

        if (isIncrement)
        {
            return (counterName, counterIncrement);
        }
        else
        {
            return (counterName, counterMean);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        for (int i = 0; i < eventData.Payload.Count; i++)
        {
            IDictionary<string, object> eventPayload = eventData.Payload[i] as IDictionary<string, object>;

            if (eventPayload != null)
            {
                var counterKV = getRelevantMetric(eventPayload);
                Console.WriteLine($"{counterKV.Name} : {counterKV.Value}");
            }
        }
    }
}
```

As shown above, you *must* make sure the `"EventCounterIntervalSec"` argument is set in the filterPayload argument when calling `EnableEvents`. Otherwise the counters will not be able to flush out values since it doesn't know at which interval it should be getting flushed out.

### Consuming out-of-proc

Consuming EventCounters out-of-proc is also possible. For those that are familiar with ETW (Event Tracing for Windows), you can use ETW to capture counter data as events and view them on your ETW trace viewer (PerfView, WPA, etc.). You may also use `dotnet-counters` to consume it cross-platform via EventPipe.

