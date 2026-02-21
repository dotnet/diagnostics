// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring.EventPipe;
using Microsoft.Diagnostics.Tests.Common;
using Microsoft.Diagnostics.Tools.Counters.Exporters;
using Xunit;
using Xunit.Abstractions;

namespace DotnetCounters.UnitTests
{
    public class ConsoleExporterTests
    {
        private readonly ITestOutputHelper _outputHelper;

        public ConsoleExporterTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        [Fact]
        public void DisplayWaitingMessage()
        {
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Waiting for initial payload...");
        }

        [Fact]
        public void DisplayEventCounter()
        {
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC since last GC", "%", 12), false);

            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                    12");
        }

        [Fact]
        public void DisplayIncrementingEventCounter()
        {
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1731), false);

            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    Allocation Rate (B / 1 sec)                    1,731");
        }

        [Fact]
        public void DisplayMultipleProviders()
        {
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1731), false);
            exporter.CounterPayloadReceived(CreateEventCounter("Provider2", "CounterXyz", "Doodads", 0.076), false);

            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[Provider2]",
                                     "    CounterXyz (Doodads)                               0.076",
                                     "[System.Runtime]",
                                     "    Allocation Rate (B / 1 sec)                    1,731");

        }

        [Fact]
        public void UpdateCounters()
        {
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            // update 1
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC since last GC", "%", 12), false);
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1731), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                    12",
                                     "    Allocation Rate (B / 1 sec)                    1,731");

            // update 2
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC since last GC", "%", 7), false);
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 123456), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                     7",
                                     "    Allocation Rate (B / 1 sec)                  123,456");
        }

        [Fact]
        public void PauseAndUnpause()
        {
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            // update 1
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC since last GC", "%", 12), false);
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1731), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                    12",
                                     "    Allocation Rate (B / 1 sec)                    1,731");

            // pause
            exporter.ToggleStatus(true);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Paused",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                    12",
                                     "    Allocation Rate (B / 1 sec)                    1,731");

            // update 2, still paused
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC since last GC", "%", 7), true);
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 123456), true);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Paused",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                    12",
                                     "    Allocation Rate (B / 1 sec)                    1,731");

            // unpause doesn't automatically update values (maybe it should??)
            exporter.ToggleStatus(false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                    12",
                                     "    Allocation Rate (B / 1 sec)                    1,731");


            // update 3 will change the values
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC since last GC", "%", 1), false);
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 2), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                     1",
                                     "    Allocation Rate (B / 1 sec)                        2");
        }

        [Fact]
        public void AlignValues()
        {
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC since last GC", "%", 0.1), false);
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1731), false);
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "BigCounter", "nanoseconds", 602341234567890123.0), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)                     0.1",
                                     "    Allocation Rate (B / 1 sec)                    1,731",
                                     "    BigCounter (nanoseconds)                      6.0234e+17");
        }

        [Fact]
        public void NameColumnWidthAdjusts()
        {
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC since last GC", "%", 0.1), false);
            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1731), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC since last GC (%)           0.1",
                                     "    Allocation Rate (B / 1 sec)          1,731");
        }

        [Fact]
        public void LongNamesAreTruncated()
        {
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "ThisCounterHasAVeryLongNameThatDoesNotFit", "%", 0.1), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[System.Runtime]",
                                     "    ThisCounterHasAVeryLongNameTha           0.1");
        }

        [Fact]
        public void MultiDimensionalCountersAreListed()
        {
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 0.1), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "size=1", 14), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "temp=hot", 160), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[Provider1]",
                                     "    Counter1 ({widget} / 1 sec)",
                                     "        color",
                                     "        -----",
                                     "        blue                                87",
                                     "        red                                  0.1",
                                     "    Counter2 ({widget} / 1 sec)",
                                     "        size temp",
                                     "        ---- ----",
                                     "        1                                   14",
                                     "             hot                           160");
        }

        [Fact]
        public void LongMultidimensionalTagsAreTruncated()
        {
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 0.1), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue,LongNameTag=ThisDoesNotFit,AnotherOne=Hi", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "size=1", 14), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "temp=hot", 160), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[Provider1]",
                                     "    Counter1 ({widget} / 1 sec)",
                                     "        color LongNameTag   herOne",
                                     "        ----- ------------- ------",
                                     "        blue  ThisDoesNotFi Hi              87",
                                     "        red                                  0.1",
                                     "    Counter2 ({widget} / 1 sec)",
                                     "        size temp",
                                     "        ---- ----",
                                     "        1                                   14",
                                     "             hot                           160");
        }

        [Fact]
        public void CountersAreTruncatedBeyondScreenHeight()
        {
            MockConsole console = new MockConsole(50, 7, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 0.1), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "size=1", 14), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "temp=hot", 160), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[Provider1]",
                                     "    Counter1 ({widget} / 1 sec)",
                                     "        color");
        }

        [Fact]
        public void MalformedTagsDoNotCrash()
        {
            // Defensive test: verifies that malformed tags are handled gracefully
            // WITHOUT the defensive fix, this test throws IndexOutOfRangeException
            // WITH the defensive fix, malformed tags are skipped and no crash occurs
            MockConsole console = new MockConsole(60, 40);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            // These would crash the old code by trying to access keyValue[1] when keyValue.Length == 1
            // Tag with no '=' - would cause IndexOutOfRangeException in old code
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "BadTagNoEquals", 10), false);
            
            // Tag with only whitespace
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "   ", 20), false);
            
            // Mix of valid and invalid tags - only valid one should be processed
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter3", "{widget}", "InvalidTag,color=blue", 30), false);

            // If we got here without throwing IndexOutOfRangeException, the defensive code worked!
            // The test passes simply by completing successfully
        }

        [Fact]
        public void ErrorStatusIsDisplayed()
        {
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 0.1), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "size=1", 14), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "temp=hot", 160), false);
            exporter.SetErrorText("Uh-oh, a bad thing happened");
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "Uh-oh, a bad thing happened",
                                     "",
                                     "Name                                 Current Value",
                                     "[Provider1]",
                                     "    Counter1 ({widget} / 1 sec)",
                                     "        color",
                                     "        -----",
                                     "        blue                                87",
                                     "        red                                  0.1",
                                     "    Counter2 ({widget} / 1 sec)",
                                     "        size temp",
                                     "        ---- ----",
                                     "        1                                   14",
                                     "             hot                           160");
        }

        [Fact]
        public void DeltaColumnDisplaysInitiallyEmpty()
        {
            MockConsole console = new MockConsole(64, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, showDeltaColumn:true);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1731), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 0.1), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "size=1", 14), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "temp=hot", 160), false);

            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                               Current Value      Last Delta",
                                     "[Provider1]",
                                     "    Counter1 ({widget} / 1 sec)",
                                     "        color",
                                     "        -----",
                                     "        blue                              87",
                                     "        red                                0.1",
                                     "    Counter2 ({widget} / 1 sec)",
                                     "        size temp",
                                     "        ---- ----",
                                     "        1                                 14",
                                     "             hot                         160",
                                     "[System.Runtime]",
                                     "    Allocation Rate (B / 1 sec)        1,731");
        }

        [Fact]
        public void DeltaColumnDisplaysNumbersAfterUpdate()
        {
            MockConsole console = new MockConsole(64, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, showDeltaColumn: true);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1731), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 0.1), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "size=1", 14), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "temp=hot", 160), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                               Current Value      Last Delta",
                                     "[Provider1]",
                                     "    Counter1 ({widget} / 1 sec)",
                                     "        color",
                                     "        -----",
                                     "        blue                              87",
                                     "        red                                0.1",
                                     "    Counter2 ({widget} / 1 sec)",
                                     "        size temp",
                                     "        ---- ----",
                                     "        1                                 14",
                                     "             hot                         160",
                                     "[System.Runtime]",
                                     "    Allocation Rate (B / 1 sec)        1,731");

            exporter.CounterPayloadReceived(CreateIncrementingEventCounter("System.Runtime", "Allocation Rate", "B", 1732), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 0.2), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "size=1", 10), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                               Current Value      Last Delta",
                                     "[Provider1]",
                                     "    Counter1 ({widget} / 1 sec)",
                                     "        color",
                                     "        -----",
                                     "        blue                              87               0",
                                     "        red                                0.2             0.1",
                                     "    Counter2 ({widget} / 1 sec)",
                                     "        size temp",
                                     "        ---- ----",
                                     "        1                                 10              -4",
                                     "             hot                         160",
                                     "[System.Runtime]",
                                     "    Allocation Rate (B / 1 sec)        1,732               1");
        }

        // Starting in .NET 8 MetricsEventSource, Meter counter instruments report both rate of change and
        // absolute value. Reporting rate in the UI was less useful for many counters than just seeing the raw
        // value. Now dotnet-counters reports these counters as absolute by default and the optional delta column
        // is available for folks who still want to visualize rate of change.
        [Fact]
        public void MeterCounterIsAbsoluteInNet8()
        {
            MockConsole console = new MockConsole(64, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, showDeltaColumn: true);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateMeterCounterPostNet8("Provider1", "Counter1", "{widget}", "color=red", 0.1), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPostNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPostNet8("Provider1", "Counter2", "{widget}", "", 14), false);

            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                               Current Value      Last Delta",
                                     "[Provider1]",
                                     "    Counter1 ({widget})",                                            // There is no longer (unit / 1 sec) here
                                     "        color",
                                     "        -----",
                                     "        blue                              87",
                                     "        red                                0.1",
                                     "    Counter2 ({widget})                   14");

            exporter.CounterPayloadReceived(CreateMeterCounterPostNet8("Provider1", "Counter1", "{widget}", "color=red", 0.2), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPostNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPostNet8("Provider1", "Counter2", "{widget}", "", 10), false);

            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                               Current Value      Last Delta",
                                     "[Provider1]",
                                     "    Counter1 ({widget})",                                            // There is no longer (unit / 1 sec) here
                                     "        color",
                                     "        -----",
                                     "        blue                              87               0",
                                     "        red                                0.2             0.1",
                                     "    Counter2 ({widget})                   10              -4");
        }


        private static CounterPayload CreateEventCounter(string provider, string displayName, string unit, double value)
        {
            return new EventCounterPayload(DateTime.MinValue, provider, displayName, displayName, unit, value, CounterType.Metric, 0, 0, "");
        }

        private static CounterPayload CreateIncrementingEventCounter(string provider, string displayName, string unit, double value)
        {
            return new EventCounterPayload(DateTime.MinValue, provider, displayName, displayName, unit, value, CounterType.Rate, 0, 1, "");
        }

        private static CounterPayload CreateMeterCounterPreNet8(string meterName, string instrumentName, string unit, string tags, double value)
        {
            return new RatePayload(new CounterMetadata(meterName, instrumentName, unit), displayName: null, displayUnits: null, tags, value, 1, DateTime.MinValue);
        }

        private static CounterPayload CreateMeterCounterPostNet8(string meterName, string instrumentName, string unit, string tags, double value)
        {
            return new CounterRateAndValuePayload(new CounterMetadata(meterName, instrumentName, unit), displayName: null, displayUnits: null, tags, rate: double.NaN, value, DateTime.MinValue);
        }

        [Fact]
        public void NoAbbreviateLargeValues()
        {
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, abbreviateLargeNumbers: false);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "% Time in GC", "%", 0.1), false);
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "BigCounter", "ns", 602341234567890123.0), false);
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "Offset", "ms", 1701200000000.0), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    % Time in GC (%)                                   0.1",
                                     "    BigCounter (ns)              602,341,234,567,890,000",
                                     "    Offset (ms)                        1,701,200,000,000");
        }

        [Fact]
        public void NoAbbreviateNarrowConsole()
        {
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, abbreviateLargeNumbers: false);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "ThisCounterHasAVeryLongNameThatDoesNotFit", "%", 0.1), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[System.Runtime]",
                                     "    ThisCounterHasAVeryLongN                 0.1");
        }

        [Fact]
        public void NoAbbreviateMultidimensionalNarrowConsole()
        {
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, abbreviateLargeNumbers: false);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 0.1), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "size=1", 14), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter2", "{widget}", "temp=hot", 160), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[Provider1]",
                                     "    Counter1 ({widget} / 1 s",
                                     "        color",
                                     "        -----",
                                     "        blue                                87",
                                     "        red                                  0.1",
                                     "    Counter2 ({widget} / 1 s",
                                     "        size temp",
                                     "        ---- ----",
                                     "        1                                   14",
                                     "             hot                           160");
        }

        [Fact]
        public void NoAbbreviateValueGrowsOnIncrementalUpdate()
        {
            // Scenario: a counter starts with a small value that fits in the 21-char
            // minimum column, then jumps to a value that also fits in 21 chars on an
            // incremental update (no new counters, so no full redraw).
            // Values up to ~10^15 with separators fit in the 21-char minimum.
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, abbreviateLargeNumbers: false);
            exporter.Initialize();

            // First payload: small value. Triggers redraw (new counter).
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "Offset", "ms", 42), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    Offset (ms)                                       42");

            // Second payload: same counter, unix ms timestamp. Incremental update only.
            // Fits within the 21-char minimum column — no spill.
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "Offset", "ms", 1701200000000.0), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    Offset (ms)                        1,701,200,000,000");
        }

        [Fact]
        public void NoAbbreviateValueOverflowTriggersRedraw()
        {
            // Scenario: a counter starts with a small value that fits in the 21-char
            // minimum column, then jumps to a value that exceeds 21 chars. The
            // incremental update detects the overflow and triggers a full redraw
            // with wider columns instead of spilling past the column edge.
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, abbreviateLargeNumbers: false);
            exporter.Initialize();

            // First payload: small value. Triggers redraw (new counter).
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "Offset", "ms", 42), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    Offset (ms)                                       42");

            // Second payload: same counter, value exceeds 21-char column (26 chars formatted).
            // Incremental path detects overflow → full redraw with _counterValueLength=26.
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "Offset", "ms", 17012000000000000.0), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    Offset (ms)                   17,012,000,000,000,000");
        }

        [Fact]
        public void NoAbbreviateLongNameAndBigValueConflict()
        {
            // Scenario: a counter has both a long name AND a big value that exceeds
            // the 21-char minimum column. The wide value column squeezes the name
            // column, causing the counter name to be truncated more aggressively
            // than it would be with smaller values.
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, abbreviateLargeNumbers: false);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "ThisCounterHasAVeryLongNameThatDoesNotFit", "ms", 17012000000000000.0), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[System.Runtime]",
                                     "    ThisCounterHasAVery 17,012,000,000,000,000");
        }

        [Fact]
        public void NoAbbreviateLongTagsAndBigValueConflict()
        {
            // Scenario: a multidimensional counter has long tag values AND big
            // counter values simultaneously. The wide value column squeezes the
            // name column, truncating the counter name and tag display.
            MockConsole console = new MockConsole(50, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, abbreviateLargeNumbers: false);
            exporter.Initialize();

            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=red", 17012000000000000.0), false);
            exporter.CounterPayloadReceived(CreateMeterCounterPreNet8("Provider1", "Counter1", "{widget}", "color=blue", 87), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                 Current Value",
                                     "[Provider1]",
                                     "    Counter1 ({widget}",
                                     "        color",
                                     "        -----",
                                     "        blue                                87",
                                     "        red             17,012,000,000,000,000");
        }

        [Fact]
        public void NoAbbreviateColumnDoesNotShrink()
        {
            // Scenario: a counter starts with a big value that causes the value
            // column to grow beyond the 21-char minimum, then the value drops to
            // a small number. The column should NOT shrink back because the
            // incremental update path does not trigger a redraw for smaller values.
            MockConsole console = new MockConsole(60, 40, _outputHelper);
            ConsoleWriter exporter = new ConsoleWriter(console, abbreviateLargeNumbers: false);
            exporter.Initialize();

            // First payload: big value, triggers redraw (new counter). Column grows to 26.
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "Offset", "ms", 17012000000000000.0), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    Offset (ms)                   17,012,000,000,000,000");

            // Second payload: small value, incremental update only. Column stays at 26.
            // The value is right-aligned in the wide column, showing the non-shrinking behavior.
            exporter.CounterPayloadReceived(CreateEventCounter("System.Runtime", "Offset", "ms", 42), false);
            console.AssertLinesEqual("Press p to pause, r to resume, q to quit.",
                                     "    Status: Running",
                                     "",
                                     "Name                                           Current Value",
                                     "[System.Runtime]",
                                     "    Offset (ms)                                       42");
        }
    }
}
