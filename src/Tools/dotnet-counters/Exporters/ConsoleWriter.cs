// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
            public double LastValue { get; set; }
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
            public double LastValue { get; set; }
        }

        private readonly object _lock = new object();
        private readonly Dictionary<string, ObservedProvider> providers = new Dictionary<string, ObservedProvider>(); // Tracks observed providers and counters.
        private const int Indent = 4; // Counter name indent size.
        private int maxNameLength = 40; // Allow room for 40 character counter names by default.

        private int STATUS_ROW; // Row # of where we print the status of dotnet-counters
        private bool paused;
        private bool initialized;
        private string _errorText;

        private int maxRow = -1;
        private bool useAnsi;

        public ConsoleWriter(bool useAnsi)
        {
            this.useAnsi = useAnsi;
        }

        public void Initialize()
        {
            AssignRowsAndInitializeDisplay();
        }

        public void EventPipeSourceConnected()
        {
            // Do nothing
        }

        public void SetErrorText(string errorText)
        {
            _errorText = errorText;
            AssignRowsAndInitializeDisplay();
        }

        private void SetCursorPosition(int col, int row)
        {
            if (this.useAnsi)
            {
                Console.Write($"\u001b[{row + 1};{col + 1}H");
            }
            else
            {
                Console.SetCursorPosition(col, row);
            }
        }

        private void Clear()
        {
            if (this.useAnsi)
            {
                Console.Write($"\u001b[H\u001b[J");
            }
            else
            {
                Console.Clear();
            }
        }
        private void UpdateStatus()
        {
            SetCursorPosition(0, STATUS_ROW);
            Console.Write($"    Status: {GetStatus()}{new string(' ', 40)}"); // Write enough blanks to clear previous status.
        }

        private string GetStatus() => !initialized ? "Waiting for initial payload..." : (paused ? "Paused" : "Running");

        /// <summary>Clears display and writes out category and counter name layout.</summary>
        public void AssignRowsAndInitializeDisplay()
        {
            Clear();

            int row = Console.CursorTop;
            Console.WriteLine("Press p to pause, r to resume, q to quit."); row++;
            Console.WriteLine($"    Status: {GetStatus()}"); STATUS_ROW = row++;
            if (_errorText != null)
            {
                Console.WriteLine(_errorText);
                row += GetLineWrappedLines(_errorText);
            }
            Console.WriteLine(); row++; // Blank line.

            foreach (ObservedProvider provider in providers.Values.OrderBy(p => p.KnownProvider == null).ThenBy(p => p.Name)) // Known providers first.
            {
                Console.WriteLine($"[{provider.Name}]"); row++;
                foreach (ObservedCounter counter in provider.Counters.Values.OrderBy(c => c.DisplayName))
                {
                    string name = MakeFixedWidth($"{new string(' ', Indent)}{counter.DisplayName}", Indent + maxNameLength);
                    counter.Row = row++;
                    if (counter.RenderValueInline)
                    {
                        Console.WriteLine($"{name} {FormatValue(counter.LastValue)}");
                    }
                    else
                    {
                        Console.WriteLine(name);
                        foreach (ObservedTagSet tagSet in counter.TagSets.Values.OrderBy(t => t.Tags))
                        {
                            string tagName = MakeFixedWidth($"{new string(' ', 2 * Indent)}{tagSet.Tags}", Indent + maxNameLength);
                            Console.WriteLine($"{tagName} {FormatValue(tagSet.LastValue)}");
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
                    if (tags != null)
                    {
                        counter.LastValue = payload.Value;
                    }
                    redraw = true;
                }

                ObservedTagSet tagSet = null;
                if (tags != null && !counter.TagSets.TryGetValue(tags, out tagSet))
                {
                    counter.TagSets[tags] = tagSet = new ObservedTagSet(tags);
                    maxNameLength = Math.Max(maxNameLength, tagSet.DisplayTags.Length);
                    tagSet.LastValue = payload.Value;
                    redraw = true;
                }

                if (redraw)
                {
                    AssignRowsAndInitializeDisplay();
                }

                int row = counter.RenderValueInline ? counter.Row : tagSet.Row;
                SetCursorPosition(Indent + maxNameLength + 1, row);
                Console.Write(FormatValue(payload.Value));
            }
        }

        private static int GetLineWrappedLines(string text)
        {
            string[] lines = text.Split(Environment.NewLine);
            int lineCount = lines.Length;
            int width = Console.BufferWidth;
            foreach (string line in lines)
            {
                lineCount += (int)Math.Floor(((float)line.Length) / width);
            }
            return lineCount;
        }

        private string FormatValue(double value)
        {
            string valueText = null;
            // The value field is one of:
            //  a) If abs(value) >= 10^9 then it is formatted as 0.####e+00
            //  b) otherwise leading - or space, 10 leading digits with separators (or spaces), decimal separator or space,
            //     3 decimal digits or spaces.
            //
            // For example:
            //   1,421,893.21
            //           0.123
            //          -0.123
            //  4.9012e+25
            //    -675,430.9
            // -12,675,430.9
            //           7


            if (Math.Abs(value) >= 100_000_000)
            {
                valueText = string.Format(CultureInfo.CurrentCulture, "{0,15:0.####e+00}   ", value);
            }
            else
            {
                string formattedVal = value.ToString("##,###,##0.###");
                int seperatorIndex = formattedVal.IndexOf(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
                if (seperatorIndex == -1)
                {
                    formattedVal += "    ";
                }
                else
                {
                    int decimalDigits = formattedVal.Length - 1 - seperatorIndex;
                    formattedVal += new string(' ', 3 - decimalDigits);
                }
                valueText = string.Format(CultureInfo.CurrentCulture, "{0,15}", formattedVal);
            }
            return valueText;
        }

        private static string MakeFixedWidth(string text, int width)
        {
            if (text.Length == width)
            {
                return text;
            }
            else if (text.Length > width)
            {
                return text.Substring(0, width);
            }
            else
            {
                return text += new string(' ', width - text.Length);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (initialized)
                {
                    int row = maxRow;

                    if (row > -1)
                    {
                        SetCursorPosition(0, row);
                        Console.WriteLine();
                    }
                }
            }
        }
    }
}
