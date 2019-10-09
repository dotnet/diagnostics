using System;
using System.Diagnostics.Tracing;
using System.Threading;
using System.Diagnostics;

namespace custom_counter
{
    /// <summary>
    /// This is the payload that is sent in the with EventSource.Write
    /// </summary>
    [EventData]
    class MyPayload
    {
        public MyPayload(int[] payload) { Payload = payload; }
        public int[] Payload { get; set; }
    }

    class Program
    {
        // Give your event sources a descriptive name using the EventSourceAttribute, otherwise the name of the class is used. 
        [EventSource(Name = "SimpleEventSource")]
        public sealed class MinimalEventCounterSource : EventSource
        {
            // define the singleton instance of the event source
            public static MinimalEventCounterSource Log = new MinimalEventCounterSource();
            private EventCounter requestCounter;
            private IncrementingEventCounter anotherCounter;

            private MinimalEventCounterSource() : base(EventSourceSettings.EtwSelfDescribingEventFormat) 
            {
                this.requestCounter = new EventCounter("request", this);
                this.anotherCounter = new IncrementingEventCounter("anotherCounter", this);
            }

            private int requestCnt = 0;

            /// <summary>
            /// Call this method to indicate that a request for a URL was made which took a particular amount of time
            public void Request()
            {
                // Notes:
                //   1. the event ID passed to WriteEvent (1) corresponds to the (implied) event ID
                //      assigned to this method. The event ID could have been explicitly declared
                //      using an EventAttribute for this method
                //   2. Each counter supports a single float value, so conceptually it maps to a single
                //      measurement in the code.
                //   3. You don't have to have log with WriteEvent if you don't think you will ever care about details
                //       of individual requests (that counter data is sufficient).
                requestCnt += 1;
                Console.WriteLine($"Request {requestCnt} came in!");
                int[] arr = new int[10];
                for(var i = 0; i < 10; i++) arr[i] = i;
                Write("hi", new EventSourceOptions() { Level = EventLevel.LogAlways }, new MyPayload(arr));
                this.requestCounter.WriteMetric(requestCnt);        // This adds it to the PerfCounter called 'Request' if PerfCounters are on
                this.anotherCounter.Increment();
            }
        }
        [EventSource(Name = "SimpleEventSource2")]
        public sealed class MinimalEventCounterSource2 : EventSource
        {
            // define the singleton instance of the event source
            public static MinimalEventCounterSource2 Log = new MinimalEventCounterSource2();
            private EventCounter requestCounter;
            private IncrementingEventCounter anotherCounter;

            private MinimalEventCounterSource2() : base(EventSourceSettings.EtwSelfDescribingEventFormat) 
            {
                this.requestCounter = new EventCounter("request", this);
                this.anotherCounter = new IncrementingEventCounter("anotherCounter", this); 
            }

            private int requestCnt = 0;

            /// <summary>
            /// Call this method to indicate that a request for a URL was made which took a particular amount of time
            public void Request()
            {
                // Notes:
                //   1. the event ID passed to WriteEvent (1) corresponds to the (implied) event ID
                //      assigned to this method. The event ID could have been explicitly declared
                //      using an EventAttribute for this method
                //   2. Each counter supports a single float value, so conceptually it maps to a single
                //      measurement in the code.
                //   3. You don't have to have log with WriteEvent if you don't think you will ever care about details
                //       of individual requests (that counter data is sufficient).
                requestCnt += 1;
                Console.WriteLine($"Request {requestCnt} came in!");
                int[] arr = new int[10];
                for(var i = 0; i < 10; i++) arr[i] = i;
                Write("hi", new EventSourceOptions() { Level = EventLevel.LogAlways }, new MyPayload(arr));
                this.requestCounter.WriteMetric(requestCnt);        // This adds it to the PerfCounter called 'Request' if PerfCounters are on
                this.anotherCounter.Increment();
            }
        }
        [EventSource(Name = "SimpleEventSource3")]
        public sealed class MinimalEventCounterSource3 : EventSource
        {
            // define the singleton instance of the event source
            public static MinimalEventCounterSource3 Log = new MinimalEventCounterSource3();
            private EventCounter requestCounter;
            private IncrementingEventCounter anotherCounter;

            private MinimalEventCounterSource3() : base(EventSourceSettings.EtwSelfDescribingEventFormat) 
            {
                this.requestCounter = new EventCounter("request", this);
                this.anotherCounter = new IncrementingEventCounter("anotherCounter", this); 
            }

            private int requestCnt = 0;

            /// <summary>
            /// Call this method to indicate that a request for a URL was made which took a particular amount of time
            public void Request()
            {
                // Notes:
                //   1. the event ID passed to WriteEvent (1) corresponds to the (implied) event ID
                //      assigned to this method. The event ID could have been explicitly declared
                //      using an EventAttribute for this method
                //   2. Each counter supports a single float value, so conceptually it maps to a single
                //      measurement in the code.
                //   3. You don't have to have log with WriteEvent if you don't think you will ever care about details
                //       of individual requests (that counter data is sufficient).
                requestCnt += 1;
                Console.WriteLine($"Request {requestCnt} came in!");
                int[] arr = new int[10];
                for(var i = 0; i < 10; i++) arr[i] = i;
                Write("hi", new EventSourceOptions() { Level = EventLevel.LogAlways }, new MyPayload(arr));
                this.requestCounter.WriteMetric(requestCnt);        // This adds it to the PerfCounter called 'Request' if PerfCounters are on
                this.anotherCounter.Increment();
            }
        }
        [EventSource(Name = "SimpleEventSource4")]
        public sealed class MinimalEventCounterSource4 : EventSource
        {
            // define the singleton instance of the event source
            public static MinimalEventCounterSource4 Log = new MinimalEventCounterSource4();
            private EventCounter requestCounter;
            private IncrementingEventCounter anotherCounter1;
            private IncrementingEventCounter anotherCounter2;
            private IncrementingEventCounter anotherCounter3;
            private IncrementingEventCounter anotherCounter4;
            private IncrementingEventCounter anotherCounter5;
            private IncrementingEventCounter anotherCounter6;
            private IncrementingEventCounter anotherCounter7;
            private IncrementingEventCounter anotherCounter8;
            private IncrementingEventCounter anotherCounter9;
            private IncrementingEventCounter anotherCounter10;
            private IncrementingEventCounter anotherCounter11;
            private IncrementingEventCounter anotherCounter12;
            private IncrementingEventCounter anotherCounter13;
            private IncrementingEventCounter anotherCounter14;


            private MinimalEventCounterSource4() : base(EventSourceSettings.EtwSelfDescribingEventFormat) 
            {
                this.requestCounter = new EventCounter("request", this);
                this.anotherCounter1 = new IncrementingEventCounter("anotherCounter1", this); 
                this.anotherCounter2 = new IncrementingEventCounter("anotherCounter2", this); 
                this.anotherCounter3 = new IncrementingEventCounter("anotherCounter3", this); 
                this.anotherCounter4 = new IncrementingEventCounter("anotherCounter4", this); 
                this.anotherCounter5 = new IncrementingEventCounter("anotherCounter5", this); 
                this.anotherCounter6 = new IncrementingEventCounter("anotherCounter6", this);
                this.anotherCounter7 = new IncrementingEventCounter("anotherCounter7", this);
                this.anotherCounter8 = new IncrementingEventCounter("anotherCounter8", this); 
                this.anotherCounter9 = new IncrementingEventCounter("anotherCounter9", this); 
                this.anotherCounter10 = new IncrementingEventCounter("anotherCounter10", this); 
                this.anotherCounter11 = new IncrementingEventCounter("anotherCounter11", this); 
                this.anotherCounter12 = new IncrementingEventCounter("anotherCounter12", this); 
                this.anotherCounter13 = new IncrementingEventCounter("anotherCounter13", this); 
                this.anotherCounter14 = new IncrementingEventCounter("anotherCounter14", this); 
                this.anotherCounter14.DisplayRateTimeScale = TimeSpan.FromSeconds(1);
            }

            private int requestCnt = 0;

            /// <summary>
            /// Call this method to indicate that a request for a URL was made which took a particular amount of time
            public void Request()
            {
                // Notes:
                //   1. the event ID passed to WriteEvent (1) corresponds to the (implied) event ID
                //      assigned to this method. The event ID could have been explicitly declared
                //      using an EventAttribute for this method
                //   2. Each counter supports a single float value, so conceptually it maps to a single
                //      measurement in the code.
                //   3. You don't have to have log with WriteEvent if you don't think you will ever care about details
                //       of individual requests (that counter data is sufficient).
                requestCnt += 1;
                Console.WriteLine($"Request {requestCnt} came in!");
                int[] arr = new int[10];
                for(var i = 0; i < 10; i++) arr[i] = i;
                Write("hi", new EventSourceOptions() { Level = EventLevel.LogAlways }, new MyPayload(arr));
                this.requestCounter.WriteMetric(requestCnt);        // This adds it to the PerfCounter called 'Request' if PerfCounters are on
                this.anotherCounter1.Increment();
                this.anotherCounter2.Increment();
                this.anotherCounter3.Increment();
                this.anotherCounter4.Increment();
                this.anotherCounter5.Increment();
                this.anotherCounter6.Increment();
                this.anotherCounter7.Increment();
                this.anotherCounter8.Increment();
                this.anotherCounter9.Increment();
                this.anotherCounter10.Increment();
                this.anotherCounter11.Increment();
                this.anotherCounter12.Increment();
                this.anotherCounter13.Increment();
                this.anotherCounter14.Increment();
            }
        }
        static void Main(string[] args)
        {
            //Debugger.Launch();
            while(true)
            {
            	MinimalEventCounterSource.Log.Request();

		MinimalEventCounterSource4.Log.Request();
		MinimalEventCounterSource2.Log.Request();
                MinimalEventCounterSource3.Log.Request();
                Thread.Sleep(1000);
            }
        }
    }
}
