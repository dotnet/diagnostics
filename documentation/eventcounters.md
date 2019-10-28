# EventCounters

## Intro
EventCounters are lightweight, cross-platform, and near real-time EventCounters are .NET Core APIs that were added to replace the "performance counters" on the .NET Framework days. EventCounters are designed to be lightweight way of measuring quick. This doc serves as a guide on what they are, how they are implemented, how to use them, and how to consume them. 

EventCounters were initially added in .NET Core 2.2 (*CHECK THIS*) but they have been changed and extended starting in .NET Core 3.0. In addition to that, the .NET Core runtime (CoreCLR) and few .NET libraries have started publishing basic diagnostics information using EventCounters starting in .NET Core 3.0. 

This document serves to explain some of the concepts behind EventCounters, and how they can be consumed both in-proc and out-of-proc with some sample code.


## Conceptual Overview


## Adding your own EventCounters

If you are writing a .NET application or library and want to keep track of some basic diagnostics information for yourself or your customers, you can easily define and add your own suite of counters. 

At a high level, there are two types of counters in terms of their *purpose* - counters for ever-increasing values (i.e. Total # of exceptions, Total # of GCs, Total # of requests, etc.) and "snapshot" values (heap usage, CPU usage, working set size, etc.). Within each of these categories of counters, there are also two types of counters for implementation details - polling counters and non-polling counters. That gives us a total of 4 different counters, and each of these can be implemented by `EventCounter`,   `PollingCounter`, `IncrementingEventCounter`, and `IncrementingPollingCounter`. 

First, `EventCounter` and `IncrementingEventCounter` capture metrics of different characteristics. `EventCounter` is meant to capture data that are more suitable as snapshot. For example, the total number of requests received for a web service can be captured by calling `EventCounter.WriteMetric(1)` every time a request is received. However, the value reported would look like:

```
"Name": "total-requests",
"Min": "1",
"Max": "1",
"Mean": "1",
"StandardDeviation": "0",
"Count": "25",
/* some more fields */
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
        Dictionary<string, string> refreshInterval = new Dictionary<string, string>();
        refreshInterval.Add("EventCounterIntervalSec", "1");
        EnableEvents(source, 1, 1, refreshInterval);
    }
}
```

#### Sample Code

This is a sample `EventListener` class that simply prints out all the counter names and values from a specified EventSource at some interval.

```cs
public class SimpleEventListener : EventListener
{        
    private readonly EventLevel _level = EventLevel.Verbose;

    public int EventCount { get; private set; } = 0;

    private string _counterSource;
    private int _intervalSec;

    public SimpleEventListener(string counterSource, int intervalSec)
    {
        _counterSource = counterSource;
        _intervalSec = intervalSec;
    }


    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name.Equals(counterSource))
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

