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
            public double? LastDelta { get; set; }
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
            public double? LastDelta { get; set; }
        }

        private readonly object _lock = new();
        private readonly Dictionary<string, ObservedProvider> _providers = new(); // Tracks observed providers and counters.
        private readonly bool _showDeltaColumn;
        private const int Indent = 4; // Counter name indent size.
        private const int CounterValueLength = 15;

        private int _nameColumnWidth; // fixed width of the name column. Names will be truncated if needed to fit in this space.
        private int _statusRow; // Row # of where we print the status of dotnet-counters
        private int _topRow;
        private bool _paused;
        private bool _initialized;
        private string _errorText;

        private int _maxRow = -1;

        private int _consoleHeight = -1;
        private int _consoleWidth = -1;
        private IConsole _console;

        public ConsoleWriter(IConsole console, bool showDeltaColumn = false)
        {
            _console = console;
            _showDeltaColumn = showDeltaColumn;
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
            // Truncate the name column if needed to prevent line wrapping
            int numValueColumns = _showDeltaColumn ? 2 : 1;
            _nameColumnWidth = Math.Max(Math.Min(80, _consoleWidth) - numValueColumns * (CounterValueLength + 1), 0);


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

            if (RenderRow(ref row) &&                                            // Blank line.
                RenderTableRow(ref row, "Name", "Current Value", "Last Delta"))  // Table header
            {
                foreach (ObservedProvider provider in _providers.Values.OrderBy(p => p.KnownProvider == null).ThenBy(p => p.Name)) // Known providers first.
                {
                    if (!RenderTableRow(ref row, $"[{provider.Name}]"))
                    {
                        break;
                    }

                    foreach (ObservedCounter counter in provider.Counters.Values.OrderBy(c => c.DisplayName))
                    {
                        counter.Row = row;
                        if (counter.RenderValueInline)
                        {
                            if (!RenderCounterValueRow(ref row, indentLevel:1, counter.DisplayName, counter.LastValue, 0))
                            {
                                break;
                            }
                        }
                        else
                        {
                            if (!RenderCounterNameRow(ref row, counter.DisplayName))
                            {
                                break;
                            }
                            foreach (ObservedTagSet tagSet in counter.TagSets.Values.OrderBy(t => t.Tags))
                            {
                                tagSet.Row = row;
                                if (!RenderCounterValueRow(ref row, indentLevel: 2, tagSet.Tags, tagSet.LastValue, 0))
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

                string providerName = payload.CounterMetadata.ProviderName;
                string name = payload.CounterMetadata.CounterName;

                string tags = payload.CombineTags();

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
                    redraw = true;
                }
                else
                {
                    counter.LastDelta = payload.Value - counter.LastValue;
                }

                ObservedTagSet tagSet = null;
                if (string.IsNullOrEmpty(tags))
                {
                    counter.LastValue = payload.Value;
                }
                else
                {
                    if (!counter.TagSets.TryGetValue(tags, out tagSet))
                    {
                        counter.TagSets[tags] = tagSet = new ObservedTagSet(tags);
                        redraw = true;
                    }
                    else
                    {
                        tagSet.LastDelta = payload.Value - tagSet.LastValue;
                    }
                    tagSet.LastValue = payload.Value;
                }

                if (_console.WindowWidth != _consoleWidth || _console.WindowHeight != _consoleHeight)
                {
                    redraw = true;
                }

                if (redraw)
                {
                    AssignRowsAndInitializeDisplay();
                }
                else
                {
                    if (tagSet != null)
                    {
                        IncrementalUpdateCounterValueRow(tagSet.Row, tagSet.LastValue, tagSet.LastDelta.Value);
                    }
                    else
                    {
                        IncrementalUpdateCounterValueRow(counter.Row, counter.LastValue, counter.LastDelta.Value);
                    }
                }
            }
        }

        public void CounterStopped(CounterPayload payload)
        {
            lock (_lock)
            {
                string providerName = payload.CounterMetadata.ProviderName;
                string counterName = payload.CounterMetadata.CounterName;
                string tags = payload.CombineTags();

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

        private bool RenderCounterValueRow(ref int row, int indentLevel, string name, double value, double? delta)
        {
            // if you change line formatting, keep it in sync with IncrementaUpdateCounterValueRow below
            string deltaText = delta.HasValue ? "" : FormatValue(delta.Value);
            return RenderTableRow(ref row, $"{new string(' ', Indent * indentLevel)}{name}", FormatValue(value), deltaText);
        }

        private bool RenderCounterNameRow(ref int row, string name)
        {
            return RenderTableRow(ref row, $"{new string(' ', Indent)}{name}");
        }

        private bool RenderTableRow(ref int row, string name, string value = null, string delta = null)
        {
            // if you change line formatting, keep it in sync with IncrementaUpdateCounterValueRow below
            string nameCellText = MakeFixedWidth(name, _nameColumnWidth);
            string valueCellText = MakeFixedWidth(value, CounterValueLength, alignRight: true);
            string deltaCellText = MakeFixedWidth(delta, CounterValueLength, alignRight: true);
            string lineText;
            if (_showDeltaColumn)
            {
                lineText = $"{nameCellText} {valueCellText} {deltaCellText}";
            }
            else
            {
                lineText = $"{nameCellText} {valueCellText}";
            }
            return RenderRow(ref row, lineText);
        }

        private bool RenderRow(ref int row, string text = null)
        {
            if (row >= _consoleHeight + _topRow) // prevents from displaying more counters than vertical space available
            {
                return false;
            }

            if (text != null)
            {
                _console.Write(text);
            }

            if (row < _consoleHeight + _topRow - 1) // prevents screen from scrolling due to newline on last line of console
            {
                _console.WriteLine();
            }

            row++;

            return true;
        }

        private void IncrementalUpdateCounterValueRow(int row, double value, double delta)
        {
            // prevents from displaying more counters than vertical space available
            if (row < 0 || row >= _consoleHeight + _topRow)
            {
                return;
            }

            _console.SetCursorPosition(_nameColumnWidth + 1, row);
            string valueCellText = MakeFixedWidth(FormatValue(value), CounterValueLength);
            string deltaCellText = MakeFixedWidth(FormatValue(delta), CounterValueLength);
            string partialLineText;
            if (_showDeltaColumn)
            {
                partialLineText = $"{valueCellText} {deltaCellText}";
            }
            else
            {
                partialLineText = $"{valueCellText}";
            }
            _console.Write(partialLineText);
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

        private static string MakeFixedWidth(string text, int width, bool alignRight = false)
        {
            if (text == null)
            {
                return new string(' ', width);
            }
            else if (text.Length == width)
            {
                return text;
            }
            else if (text.Length > width)
            {
                return text.Substring(0, width);
            }
            else
            {
                if (alignRight)
                {
                    return new string(' ', width - text.Length) + text;
                }
                else
                {
                    return text + new string(' ', width - text.Length);
                }
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
            }
        }
    }
}
