// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        }

        private readonly Dictionary<string, ObservedProvider> providers = new Dictionary<string, ObservedProvider>(); // Tracks observed providers and counters.
        private const int Indent = 4; // Counter name indent size.
        private int maxNameLength = 40; // Allow room for 40 character counter names by default.
        private int maxPreDecimalDigits = 11; // Allow room for values up to 999 million by default.

        private int STATUS_ROW; // Row # of where we print the status of dotnet-counters
        private bool paused = false;
        private bool initialized = false;

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
                }
            }
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

        public void CounterPayloadReceived(string providerName, ICounterPayload payload, bool pauseCmdSet)
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

            string name = payload.GetName();

            bool redraw = false;
            if (!providers.TryGetValue(providerName, out ObservedProvider provider))
            {
                providers[providerName] = provider = new ObservedProvider(providerName);
                redraw = true;
            }

            if (!provider.Counters.TryGetValue(name, out ObservedCounter counter))
            {
                string displayName = payload.GetDisplay();
                provider.Counters[name] = counter = new ObservedCounter(displayName);
                maxNameLength = Math.Max(maxNameLength, displayName.Length);
                redraw = true;
            }

            const string DecimalPlaces = "###";
            string payloadVal = payload.GetValue().ToString("#,0." + DecimalPlaces, CultureInfo.CurrentCulture);
            int decimalIndex = payloadVal.IndexOf(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, StringComparison.CurrentCulture);
            if (decimalIndex == -1)
            {
                decimalIndex = payloadVal.Length;
            }

            if (decimalIndex > maxPreDecimalDigits)
            {
                maxPreDecimalDigits = decimalIndex;
                redraw = true;
            }

            if (redraw)
            {
                AssignRowsAndInitializeDisplay();
            }

            Console.SetCursorPosition(Indent + maxNameLength + 1, counter.Row);
            int prefixSpaces = maxPreDecimalDigits - decimalIndex;
            int postfixSpaces = DecimalPlaces.Length - (payloadVal.Length - decimalIndex - 1);
            Console.Write($"{new string(' ', prefixSpaces)}{payloadVal}{new string(' ', postfixSpaces)}");
        }

        public void Stop()
        {
            // Nothing to do here.
        }
    }
}
