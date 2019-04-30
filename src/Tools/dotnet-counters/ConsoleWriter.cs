// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;


namespace Microsoft.Diagnostics.Tools.Counters
{
    public class ConsoleWriter
    {
        private Dictionary<string, (int, int)> displayPosition; // Display position (x-y coordiates) of each counter values.
        private int origRow;
        private int origCol;
        private int maxRow;  // Running maximum of row number
        private int maxCol;  // Running maximum of col number
        private Dictionary<string, int> knownProvidersRowNum;

        public ConsoleWriter()
        {
            displayPosition = new Dictionary<string, (int, int)>();
            knownProvidersRowNum = new Dictionary<string, int>();

            foreach(CounterProvider provider in KnownData.GetAllProviders())
            {
                knownProvidersRowNum[provider.Name] = -1;
            }
        }

        public void InitializeDisplay()
        {
            Console.Clear();
            origRow = Console.CursorTop;
            origCol = Console.CursorLeft;
            Console.WriteLine("Press p to pause, r to resume, q to quit.");

            maxRow = origRow+1;
            maxCol = origCol;
        }

        public void Update(string providerName, ICounterPayload payload)
        {
            string name = payload.GetName();

            // We already know what this counter is! Just update the value string on the console.
            if (displayPosition.ContainsKey(name))
            {
                (int left, int row) = displayPosition[name];
                Console.SetCursorPosition(left, row);
                Console.Write(new String(' ', 8));

                Console.SetCursorPosition(left, row);
                Console.Write(payload.GetValue());  
            }
            // Got a payload from a new counter that hasn't been written to the console yet.
            else
            {
                bool isWellKnownProvider = knownProvidersRowNum.ContainsKey(providerName);

                if (isWellKnownProvider)
                {
                    if (knownProvidersRowNum[providerName] < 0)
                    {
                        knownProvidersRowNum[providerName] = maxRow + 1;
                        Console.SetCursorPosition(0, maxRow);
                        Console.WriteLine(providerName);
                        maxRow += 1;
                    }

                    KnownData.TryGetProvider(providerName, out CounterProvider counterProvider);
                    string displayName = counterProvider.TryGetDisplayName(name);
                    if (displayName == null)
                    {
                        displayName = payload.GetDisplay();
                    }
                    
                    int left = displayName.Length + 7; // displayName + " : "
                    int row = maxRow;
                    displayPosition[name] = (left, row);
                    Console.WriteLine($"    {displayName} : {payload.GetValue()}");
                    maxRow += 1;
                }
                else
                {
                    // If it's from an unknown provider, just append it at the end.
                    string displayName = payload.GetDisplay();
                    if (string.IsNullOrEmpty(displayName))
                    {
                        displayName = payload.GetName();
                    }
                    int left = displayName.Length + 7; // displayName + " : "
                    int row = maxRow;
                    displayPosition[name] = (left, row);
                    Console.WriteLine($"    {displayName} : {payload.GetValue()}");
                    maxRow += 1;
                }
            }
        }
    }
}