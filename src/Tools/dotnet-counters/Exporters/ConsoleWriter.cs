// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Diagnostics.Monitoring.EventPipe;

namespace Microsoft.Diagnostics.Tools.Counters.Exporters
{
    /// <summary>
    /// ConsoleWriter is an implementation of ICounterRenderer for rendering the counter values in real-time
    /// to the console. This is the renderer for the `dotnet-counters monitor` command.
    /// </summary>
    internal class ConsoleWriter : ICounterRenderer
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

        private interface ICounterRow
        {
            int Row { get; set; }
        }

        /// <summary>Information about an observed counter.</summary>
        private class ObservedCounter : ICounterRow
        {
            public ObservedCounter(string displayName) => DisplayName = displayName;
            public string DisplayName { get; } // Display name for this counter.
            public int Row { get; set; } // Assigned row for this counter. May change during operation.
            public Dictionary<string, ObservedTagSet> TagSets { get; } = new Dictionary<string, ObservedTagSet>();

            public bool RenderValueInline => TagSets.Count == 0 ||
                       (TagSets.Count == 1 && string.IsNullOrEmpty(TagSets.Keys.First()));
            public double LastValue { get; set; }
        }

        private class ObservedTagSet : ICounterRow
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

        private readonly object _lock = new();
        private readonly Dictionary<string, ObservedProvider> _providers = new(); // Tracks observed providers and counters.
        private const int Indent = 4; // Counter name indent size.
        private const int CounterValueLength = 15;

        private int _maxNameLength;
        private int _statusRow; // Row # of where we print the status of dotnet-counters
        private int _topRow;
        private bool _paused;
        private bool _initialized;
        private string _errorText;

        private int _maxRow = -1;

        private int _consoleHeight = -1;
        private int _consoleWidth = -1;
        private IConsole _console;
        private bool _oldCursorVisibility = true;

        public ConsoleWriter(IConsole console)
        {
            _console = console;
        }

        public void Initialize()
        {
            try
            {
                _oldCursorVisibility = _console.CursorVisible;
                _console.CursorVisible = false;
            }
            // if it isn't supported then we just leave it showing. Its only aesthetic.
            catch (NotSupportedException) { }

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

        private void UpdateStatus()
        {
            _console.SetCursorPosition(0, _statusRow);
            _console.Write($"    Status: {GetStatus()}{new string(' ', 40)}"); // Write enough blanks to clear previous status.
        }

        private string GetStatus() => !_initialized ? "Waiting for initial payload..." : (_paused ? "Paused" : "Running");

        /// <summary>Clears display and writes out category and counter name layout.</summary>
        public void AssignRowsAndInitializeDisplay()
        {
            _console.Clear();

            // clear row data on all counters
            foreach (ObservedProvider provider in _providers.Values)
            {
                foreach (ObservedCounter counter in provider.Counters.Values)
                {
                    counter.Row = -1;
                    foreach (ObservedTagSet tagSet in counter.TagSets.Values)
                    {
                        tagSet.Row = -1;
                    }
                }
            }

            _consoleWidth = _console.WindowWidth;
            _consoleHeight = _console.WindowHeight;
            _maxNameLength = Math.Max(Math.Min(80, _consoleWidth) - (CounterValueLength + Indent + 1), 0); // Truncate the name to prevent line wrapping as long as the console width is >= CounterValueLength + Indent + 1 characters


            int row = _console.CursorTop;
            _topRow = row;

            string instructions = "Press p to pause, r to resume, q to quit.";
            _console.WriteLine((instructions.Length < _consoleWidth) ? instructions : instructions.Substring(0, _consoleWidth)); row++;
            _console.WriteLine($"    Status: {GetStatus()}"); _statusRow = row++;
            if (_errorText != null)
            {
                _console.WriteLine(_errorText);
                row += GetLineWrappedLines(_errorText);
            }

            bool RenderRow(ref int row, string lineOutput = null, ICounterRow counterRow = null)
            {
                if (row >= _consoleHeight + _topRow) // prevents from displaying more counters than vertical space available
                {
                    return false;
                }

                if (lineOutput != null)
                {
                    _console.Write(lineOutput);
                }

                if (row < _consoleHeight + _topRow - 1) // prevents screen from scrolling due to newline on last line of console
                {
                    _console.WriteLine();
                }

                if (counterRow != null)
                {
                    counterRow.Row = row;
                }

                row++;

                return true;
            }

            if (RenderRow(ref row)) // Blank line.
            {
                foreach (ObservedProvider provider in _providers.Values.OrderBy(p => p.KnownProvider == null).ThenBy(p => p.Name)) // Known providers first.
                {
                    if (!RenderRow(ref row, $"[{provider.Name}]"))
                    {
                        break;
                    }

                    foreach (ObservedCounter counter in provider.Counters.Values.OrderBy(c => c.DisplayName))
                    {
                        string name = MakeFixedWidth($"{new string(' ', Indent)}{counter.DisplayName}", Indent + _maxNameLength);
                        if (counter.RenderValueInline)
                        {
                            if (!RenderRow(ref row, $"{name} {FormatValue(counter.LastValue)}", counter))
                            {
                                break;
                            }
                        }
                        else
                        {
                            if (!RenderRow(ref row, name, counter))
                            {
                                break;
                            }
                            foreach (ObservedTagSet tagSet in counter.TagSets.Values.OrderBy(t => t.Tags))
                            {
                                string tagName = MakeFixedWidth($"{new string(' ', 2 * Indent)}{tagSet.Tags}", Indent + _maxNameLength);
                                if (!RenderRow(ref row, $"{tagName} {FormatValue(tagSet.LastValue)}", tagSet))
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            _maxRow = Math.Max(_maxRow, row);
        }

        public void ToggleStatus(bool pauseCmdSet)
        {
            if (_paused == pauseCmdSet)
            {
                return;
            }

            _paused = pauseCmdSet;
            UpdateStatus();
        }

        public void CounterPayloadReceived(CounterPayload payload, bool pauseCmdSet)
        {
            lock (_lock)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    AssignRowsAndInitializeDisplay();
                }

                if (pauseCmdSet)
                {
                    return;
                }

                string providerName = payload.Provider;
                string name = payload.Name;
                string tags = payload.Metadata;

                bool redraw = false;
                if (!_providers.TryGetValue(providerName, out ObservedProvider provider))
                {
                    _providers[providerName] = provider = new ObservedProvider(providerName);
                    redraw = true;
                }

                if (!provider.Counters.TryGetValue(name, out ObservedCounter counter))
                {
                    string displayName = payload.GetDisplay();
                    provider.Counters[name] = counter = new ObservedCounter(displayName);
                    _maxNameLength = Math.Max(_maxNameLength, displayName.Length);
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
                    _maxNameLength = Math.Max(_maxNameLength, tagSet.DisplayTags.Length);
                    tagSet.LastValue = payload.Value;
                    redraw = true;
                }

                if (_console.WindowWidth != _consoleWidth || _console.WindowHeight != _consoleHeight)
                {
                    redraw = true;
                }

                if (redraw)
                {
                    AssignRowsAndInitializeDisplay();
                }

                int row = counter.RenderValueInline ? counter.Row : tagSet.Row;
                if (row < 0)
                {
                    return;
                }
                _console.SetCursorPosition(Indent + _maxNameLength + 1, row);
                _console.Write(FormatValue(payload.Value));
            }
        }

        public void CounterStopped(CounterPayload payload)
        {
            lock (_lock)
            {
                string providerName = payload.Provider;
                string counterName = payload.Name;
                string tags = payload.Metadata;

                if (!_providers.TryGetValue(providerName, out ObservedProvider provider))
                {
                    return;
                }

                if (!provider.Counters.TryGetValue(counterName, out ObservedCounter counter))
                {
                    return;
                }

                ObservedTagSet tagSet = null;
                if (tags != null)
                {
                    if (!counter.TagSets.TryGetValue(tags, out tagSet))
                    {
                        return;
                    }
                    else
                    {
                        counter.TagSets.Remove(tags);
                        if (counter.TagSets.Count == 0)
                        {
                            provider.Counters.Remove(counterName);
                        }
                    }
                }
                else
                {
                    provider.Counters.Remove(counterName);
                }
                AssignRowsAndInitializeDisplay();
            }
        }

        private int GetLineWrappedLines(string text)
        {
            string[] lines = text.Split(Environment.NewLine);
            int lineCount = lines.Length;
            int width = _console.BufferWidth;
            foreach (string line in lines)
            {
                lineCount += (int)Math.Floor(((float)line.Length) / width);
            }
            return lineCount;
        }

        private static string FormatValue(double value)
        {
            string valueText;
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
                if (_initialized)
                {
                    int row = _maxRow;

                    if (row > -1)
                    {
                        _console.SetCursorPosition(0, row);
                        _console.WriteLine();
                    }
                }

                try
                {
                    _console.CursorVisible = _oldCursorVisibility;
                }
                catch (NotSupportedException) { }
            }
        }
    }
}
