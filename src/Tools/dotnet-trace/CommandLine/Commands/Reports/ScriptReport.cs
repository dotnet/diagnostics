// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Tools.Common;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using System.IO;
using System.CommandLine.IO;
using System.Collections.Concurrent;
using System.Globalization;

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal static class ScriptReportHandler
    {
        private delegate Task<int> ScriptReportDelegate(CancellationToken ct, IConsole console, string traceFile, string filter, bool verbose, ScriptFormat format);
        private static async Task<int> ScriptReport(CancellationToken ct, IConsole console, string traceFile, string filter, bool verbose, ScriptFormat format)
        {
            SimpleLogger.Log.MinimumLevel = verbose ? Microsoft.Extensions.Logging.LogLevel.Information : Microsoft.Extensions.Logging.LogLevel.Error;

            // Validate
            if (!File.Exists(traceFile))
            {
                console.Error.WriteLine($"The file '{traceFile}' doesn't exist.");
                return -1;
            }

            string noSampleProfilerFilter = $"-Microsoft-DotNETCore-SampleProfiler;{filter}";

            // Parse the filter string
            Func<TraceEvent, bool> predicate = PredicateBuilder.ParseFilter(noSampleProfilerFilter);

            using EventPipeEventSource source = new(traceFile);

            IEventSerializer serializer = format switch
            {
                ScriptFormat.Inline => new InlineEventSerializer(console.Out),
                ScriptFormat.Json   => new JsonEventSerializer(console.Out),
                _ => throw new ArgumentException("Invalid format", nameof(format))
            };

            await ProcessEvents(source, predicate, serializer);

            return 0;
        }

        public static async Task ProcessEvents(EventPipeEventSource source, Func<TraceEvent, bool> predicate, IEventSerializer writer)
        {
            int total = 0;
            string commandline = "";
            string osInformation = "";
            string archInformation = "";

            using BlockingCollection<TraceEvent> workQueue = new();

            Task eventReaderTask = Task.Run(() =>
            {
                void HandleData(TraceEvent data)
                {
                    total++;
                    if (data.ProviderName.Equals("Microsoft-DotNETCore-EventPipe", StringComparison.InvariantCultureIgnoreCase) &&
                        data.EventName.Equals("ProcessInfo", StringComparison.InvariantCultureIgnoreCase))
                    {
                        commandline = (string)data.PayloadByName("CommandLine");
                        osInformation = (string)data.PayloadByName("OSInformation");
                        archInformation = (string)data.PayloadByName("ArchInformation");
                    }

                    if (predicate(data))
                    {
                        workQueue.Add(data.Clone());
                    }
                }

                source.Dynamic.All += HandleData;

                source.Clr.All += HandleData;

                var parser = new Tracing.Parsers.ClrPrivateTraceEventParser(source);
                parser.All += HandleData;

                var rundownParser = new Tracing.Parsers.Clr.ClrRundownTraceEventParser(source);
                rundownParser.All += HandleData;

                source.Process();

                workQueue.CompleteAdding();
            });

            Task eventSerializerTask = Task.Run(() =>
            {
                writer.WritePrologue();
                writer.WriteEventsStart();
                try
                {
                    // Consume consume the BlockingCollection
                    while (true)
                    {
                        writer.WriteEvent(workQueue.Take());
                    }
                }
                catch (InvalidOperationException)
                {
                    // An InvalidOperationException means that Take() was called on a completed collection
                    writer.WriteEventsEnd();
                }
                writer.WriteEpilogue();
            });

            await Task.WhenAll(eventReaderTask, eventSerializerTask);
        }

        private static void PrintStats(IConsole console, EventPipeEventSource source, Dictionary<string, int> stats, string traceFile, int total, string commandline, string osInformation, string archInformation)
        {
            string divider = new('-', 120);
            // Print header info
            const int headerKeyAlignment = -25;
            const int headerValAlignment = -95;
            console.Out.WriteLine($"{"Trace name:",headerKeyAlignment}{traceFile,headerValAlignment}");
            console.Out.WriteLine($"{"Commandline:",headerKeyAlignment}{commandline,headerValAlignment}");
            console.Out.WriteLine($"{"OS:",headerKeyAlignment}{osInformation,headerValAlignment}");
            console.Out.WriteLine($"{"Architecture:",headerKeyAlignment}{archInformation,headerValAlignment}");
            console.Out.WriteLine($"{"Trace start time:",headerKeyAlignment}{source.SessionStartTime.ToLocalTime(),headerValAlignment}");
            console.Out.WriteLine($"{"Trace Duration:",headerKeyAlignment}{source.SessionDuration,headerValAlignment:c}");
            console.Out.WriteLine($"{"Number of processors:",headerKeyAlignment}{source.NumberOfProcessors,headerValAlignment}");
            console.Out.WriteLine($"{"Events Lost:",headerKeyAlignment}{source.EventsLost,headerValAlignment}");
            console.Out.WriteLine($"{"Total Events:",headerKeyAlignment}{total,headerValAlignment}");
            console.Out.WriteLine($"{"Filtered Events:",headerKeyAlignment}{stats.Values.Sum(),headerValAlignment}");
            console.Out.WriteLine(divider);
            console.Out.WriteLine();

            const int bodyKeyAlignment = -80;
            const int bodyValAlignment = 10;
            foreach ((string key, int val) in stats)
                console.Out.WriteLine($"{$"{key}:",bodyKeyAlignment}{val,bodyValAlignment:N0}");
        }

        private const string DescriptionString = @$"Filter the report output. Syntax:
Filter ::= 𝞮 | <NameFilter> | <NameFilter>:<Subfilter> | -<Filter> | <Filter>;<Filter>
Subfilter ::= id=<Number> | name=<NameFilter> | keyword=<Number>
NameFilter ::= [a-zA-Z0-9\*]*
Number ::= [1-9]+[0-9]* | 0x[0-9a-fA-F]* | 0b[01]*

Examples:
* 'Microsoft-Windows-DotNETRuntime' - only show stats for this provider
* 'Microsoft-Windows-DotNETRuntime:name=Jit*' - only show stats for Jit events from this provider
* 'Microsoft-Windows-DotNETRuntime:name=Jit*;MyProvider:keyword=0xFFF' - only show stats for Jit events from this provider and events with keyword 0xFFF from the other
* '-ProviderA' - don't show events from ProviderA
* '-Microsoft*' - don't show events from Microsoft* providers
";

        public enum ScriptFormat
        {
            Inline,
            Json
        }

        private static Option FormatOption() =>
            new Option(
                aliases: new[] { "--format" },
                description: "Format to print the events in.")
                {
                    Argument = new Argument<ScriptFormat>(name: "format", getDefaultValue: () => ScriptFormat.Inline)
                };

        private static Option FilterOption() =>
            new Option(
                aliases: new[] { "--filter" },
                description: DescriptionString)
                {
                    Argument = new Argument<string>(name: "filter", getDefaultValue: () => "")
                };

        private static Option VerboseOption() =>
            new Option(
                aliases: new[] {"-v", "--verbose"},
                description: $"Output additional information from filter parsing.")
                {
                    Argument = new Argument<bool>(name: "verbose", getDefaultValue: () => false)
                };

        public static Command ScriptCommand =>
            new Command(
                name: "script",
                description: "Display events and metadata in the trace.")
                {
                    //Handler
                    HandlerDescriptor.FromDelegate((ScriptReportDelegate)ScriptReport).GetCommandHandler(),
                    FilterOption(),
                    FormatOption(),
                    VerboseOption(),
                    ReportCommandHandler.FileNameArgument()
                };
    }

    internal interface IEventSerializer
    {
        void WritePrologue();
        void WriteEventsStart();
        void WriteEvent(TraceEvent data);
        void WriteEventsEnd();
        void WriteEpilogue();
        void WriteKeyValuePair<T>(string key, T value);
    }

    internal class InlineEventSerializer : IEventSerializer
    {
        private readonly IStandardStreamWriter writer;
        public InlineEventSerializer(IStandardStreamWriter writer)
        {
            this.writer = writer;
        }

        public void WriteEpilogue() { }

        public void WriteEvent(TraceEvent data)
        {
            // console.Out.WriteLine(data.ToString());
            writer.Write($"[{TimeSpan.FromMilliseconds(data.TimeStampRelativeMSec):c}] {data.ProviderName}/{data.EventName} :: ");
            foreach (string name in data.PayloadNames)
                writer.Write($"{name}=\"{data.PayloadByName(name)}\" ");

            string formattedMessage = data.GetFormattedMessage(new CultureInfo("en-US"));
            if (!string.IsNullOrEmpty(formattedMessage))
                writer.Write($"message=\"{formattedMessage}\"");

            writer.WriteLine();
        }

        public void WriteEventsEnd() { }

        public void WriteEventsStart() { }

        public void WriteKeyValuePair<T>(string key, T value)
        {
            writer.WriteLine($"{key}: {value}");
        }

        public void WritePrologue() { }
    }

    internal class JsonEventSerializer : IEventSerializer
    {
        private readonly IStandardStreamWriter writer;
        public JsonEventSerializer(IStandardStreamWriter writer)
        {
            this.writer = writer;
        }

        public void WriteEpilogue() => writer.WriteLine("}");

        public void WriteEvent(TraceEvent data)
        {
            writer.Write("\t\t{ ");
            // writer.Out.WriteLine(data.ToString());
            writer.Write($"\"RelativeTimeMS\": {data.TimeStampRelativeMSec}, \"ProviderName\": \"{data.ProviderName}\", \"EventName\", \"{data.EventName}\"");
            foreach (string name in data.PayloadNames)
                writer.Write($", \"{name}\": {System.Text.Json.JsonSerializer.Serialize(data.PayloadByName(name))}");

            string formattedMessage = data.GetFormattedMessage(new CultureInfo("en-US"));
            if (!string.IsNullOrEmpty(formattedMessage))
                writer.Write($", \"Message\": \"{formattedMessage}\"");

            writer.WriteLine(" },");
        }

        public void WriteEventsEnd() =>  writer.WriteLine("\t],");

        public void WriteEventsStart() => writer.WriteLine("\t\"Events\": [");

        public void WriteKeyValuePair<T>(string key, T value)
        {
            writer.WriteLine($"\t\"{key}\": {System.Text.Json.JsonSerializer.Serialize(value)},");
        }

        public void WritePrologue() => writer.WriteLine("{");
    }
}