// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class ConsoleWriter
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

        private void UpdateStatus(string msg)
        {
            Console.SetCursorPosition(0, STATUS_ROW);
            Console.Write(new String(' ', 42)); // Length of the initial string we print on the console..
            Console.SetCursorPosition(0, STATUS_ROW);
            Console.Write(msg);
        }

        /// <summary>Clears display and writes out category and counter name layout.</summary>
        public void AssignRowsAndInitializeDisplay()
        {
            Console.Clear();
            int row = Console.CursorTop;
            Console.WriteLine("Press p to pause, r to resume, q to quit.");               row++;
            Console.WriteLine("    Status: Waiting for initial payload..."); STATUS_ROW = row++;
            Console.WriteLine();                                                          row++; // Blank line.

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
            else if (pauseCmdSet)
            {
                UpdateStatus("    Status: Paused");
            }
            else
            {
                UpdateStatus("    Status: Running");
            }
            paused = pauseCmdSet;
        }

        public void Update(string providerName, ICounterPayload payload, bool pauseCmdSet)
        {
            if (!initialized)
            {
                AssignRowsAndInitializeDisplay();
                initialized = true;
                UpdateStatus("    Status: Running");
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
                string displayName = provider.KnownProvider?.TryGetDisplayName(name) ?? payload.GetDisplay() ?? name;
                provider.Counters[name] = counter = new ObservedCounter(displayName);
                maxNameLength = Math.Max(maxNameLength, displayName.Length);
                redraw = true;
            }

            const string DecimalPlaces = "###";
            string payloadVal = payload.GetValue().ToString("#,0." + DecimalPlaces);
            int decimalIndex = payloadVal.IndexOf('.');
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
    }
}
