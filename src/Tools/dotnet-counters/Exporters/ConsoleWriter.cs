// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    /// <summary>
    /// ConsoleWriter is an implementation of ICounterRenderer for rendering the counter values in real-time
    /// to the console. This is the renderer for the `dotnet-counters monitor` command.
    /// </summary>
    public class ConsoleWriter : ICounterRenderer
    {
        /// <summary>Information about an observed provider.</summary>
        private class ObservedProvider
        {
            public ObservedProvider(string name)
            {
                Name = name;
                KnownData.TryGetProvider(name, out KnownProvider);
            }

            public string Name { get; } // Name of the category.
            public Dictionary<string, ObservedCounter> Counters { get; } = new Dictionary<string, ObservedCounter>(); // Counters in this category.
            public readonly CounterProvider KnownProvider;
        }

        /// <summary>Information about an observed counter.</summary>
        private class ObservedCounter
        {
            public ObservedCounter(string displayName) => DisplayName = displayName;
            public string DisplayName { get; } // Display name for this counter.
            public int Row { get; set; } // Assigned row for this counter. May change during operation.
            public Dictionary<string, ObservedTagSet> TagSets { get; } = new Dictionary<string, ObservedTagSet>();

            public bool RenderValueInline => TagSets.Count == 0 ||
                       (TagSets.Count == 1 && string.IsNullOrEmpty(TagSets.Keys.First()));
        }

        private class ObservedTagSet
        {
            public ObservedTagSet(string tags)
            {
                Tags = tags;
            }
            public string Tags { get; }
            public string DisplayTags => string.IsNullOrEmpty(Tags) ? "<no tags>" : Tags;
            public int Row { get; set; } // Assigned row for this counter. May change during operation.
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, ObservedProvider> providers = new Dictionary<string, ObservedProvider>(); // Tracks observed providers and counters.
        private const int Indent = 4; // Counter name indent size.
        private int maxNameLength = 40; // Allow room for 40 character counter names by default.

        private int STATUS_ROW; // Row # of where we print the status of dotnet-counters
        private bool paused = false;
        private bool initialized = false;

        private int maxRow = -1;

        public void Initialize()
        {
            AssignRowsAndInitializeDisplay();
        }

        public void EventPipeSourceConnected()
        {
            // Do nothing
        }

        private void UpdateStatus()
        {
            Console.SetCursorPosition(0, STATUS_ROW);
            Console.Write($"    Status: {GetStatus()}{new string(' ', 40)}"); // Write enough blanks to clear previous status.
        }

        private string GetStatus() => !initialized ? "Waiting for initial payload..." : (paused ? "Paused" : "Running");

        /// <summary>Clears display and writes out category and counter name layout.</summary>
        public void AssignRowsAndInitializeDisplay()
        {
            Console.Clear();
            int row = Console.CursorTop;
            Console.WriteLine("Press p to pause, r to resume, q to quit."); row++;
            Console.WriteLine($"    Status: {GetStatus()}");                STATUS_ROW = row++;
            Console.WriteLine();                                            row++; // Blank line.

            foreach (ObservedProvider provider in providers.Values.OrderBy(p => p.KnownProvider == null).ThenBy(p => p.Name)) // Known providers first.
            {
                Console.WriteLine($"[{provider.Name}]"); row++;
                foreach (ObservedCounter counter in provider.Counters.Values.OrderBy(c => c.DisplayName))
                {
                    Console.WriteLine($"{new string(' ', Indent)}{counter.DisplayName}");
                    counter.Row = row++;
                    if(!counter.RenderValueInline)
                    {
                        foreach (ObservedTagSet tagSet in counter.TagSets.Values.OrderBy(t => t.Tags))
                        {
                            Console.WriteLine($"{new string(' ', 2 * Indent)}{tagSet.Tags}");
                            tagSet.Row = row++;
                        }
                    }
                }
            }

            maxRow = Math.Max(maxRow, row);
        }

        public void ToggleStatus(bool pauseCmdSet)
        {
            if (paused == pauseCmdSet)
            {
                return;
            }

            paused = pauseCmdSet;
            UpdateStatus();
        }

        public void CounterPayloadReceived(CounterPayload payload, bool pauseCmdSet)
        {
            lock (_lock)
            {
                if (!initialized)
                {
                    initialized = true;
                    AssignRowsAndInitializeDisplay();
                }

                if (pauseCmdSet)
                {
                    return;
                }

                string providerName = payload.ProviderName;
                string name = payload.Name;
                string tags = payload.Tags;

                bool redraw = false;
                if (!providers.TryGetValue(providerName, out ObservedProvider provider))
                {
                    providers[providerName] = provider = new ObservedProvider(providerName);
                    redraw = true;
                }

                if (!provider.Counters.TryGetValue(name, out ObservedCounter counter))
                {
                    string displayName = payload.DisplayName;
                    provider.Counters[name] = counter = new ObservedCounter(displayName);
                    maxNameLength = Math.Max(maxNameLength, displayName.Length);
                    redraw = true;
                }

                ObservedTagSet tagSet = null;
                if (tags != null && !counter.TagSets.TryGetValue(tags, out tagSet))
                {
                    counter.TagSets[tags] = tagSet = new ObservedTagSet(tags);
                    maxNameLength = Math.Max(maxNameLength, tagSet.DisplayTags.Length);
                    redraw = true;
                }

                
                StringBuilder valueText = new StringBuilder();
                // The value field is formatted:
                // [Optional name][   value     ]             [optional name][     value   ] 
                // |<--5 chars-->||<--15 chars->||<-4 chars->||<--5 chars-->||<--15 chars->||<-4 chars->| ...
                // Each optional name is either a blank or some formatted string like "P50: "
                // Each value is one of:
                //  a) If abs(value) >= 10^9 then it is formatted as 0.########e+00
                //  b) otherwise leading - or space, 10 leading digits with separators (or spaces), decimal separator or space,
                //     3 decimal digits or spaces.
                //
                // For example:
                // |    |              |   |    |              |   |    |              |
                // P50:   1,421,893.21     P95:  19,000,000.001    P99:    4.000123e+25
                // P50:           0.123    P95:          10.432    P99:         123.456
                // P50:          -0.123    P95:         -10.432    P99:        -123.456
                //         -675,430.9    
                //      -12,675,430.9  
                //                7

                for (int i = 0; i < payload.Values.Length; i++)
                {
                    string key = payload.Values[i].Key;
                    if(!string.IsNullOrEmpty(key))
                    {
                        key = key + ": ";
                    }
                    else
                    {
                        key = "";
                    }
                    double val = payload.Values[i].Value;
                    if (Math.Abs(val) >= 100_000_000)
                    {
                        valueText.AppendFormat(CultureInfo.CurrentCulture, "{0,5}{1,15:0.######e+00}    ", key, val);
                    }
                    else
                    {
                        string formattedVal = val.ToString("##,###,##0.###");
                        int seperatorIndex = formattedVal.IndexOf(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
                        if(seperatorIndex == -1)
                        {
                            formattedVal += "    ";
                        }
                        else
                        {
                            int decimalDigits = formattedVal.Length - 1 - seperatorIndex;
                            formattedVal += new string(' ', 3 - decimalDigits);
                        }
                        valueText.AppendFormat(CultureInfo.CurrentCulture, "{0,5}{1,15}    ", key, formattedVal);
                    }
                }

                string payloadVal = valueText.ToString();
                const int valueFixedLength = 68;
                if (payloadVal.Length > valueFixedLength)
                {
                    payloadVal = payloadVal.Substring(0, valueFixedLength);
                }

                if (redraw)
                {
                    AssignRowsAndInitializeDisplay();
                }

                int row = counter.RenderValueInline ? counter.Row : tagSet.Row;
                Console.SetCursorPosition(Indent + maxNameLength + 1, row);
                Console.Write(payloadVal);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (initialized)
                {
                    var row = maxRow;

                    if (row > -1)
                    {
                        Console.SetCursorPosition(0, row);
                        Console.WriteLine();
                    }
                }
            }
        }
    }
}
