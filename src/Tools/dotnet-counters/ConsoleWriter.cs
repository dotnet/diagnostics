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
        private Dictionary<string, int> displayLength; // Length of the counter values displayed for each counter.
        private int origRow;
        private int origCol;
        private int maxRow;  // Running maximum of row number
        private int maxCol;  // Running maximum of col number
        private int STATUS_ROW; // Row # of where we print the status of dotnet-counters
        private int leftAlign;
        private bool paused = false;
        private bool initialized = false;
        private Dictionary<string, int> knownProvidersRowNum;
        private Dictionary<string, int> unknownProvidersRowNum;

        private void UpdateStatus(string msg)
        {
            Console.SetCursorPosition(0, STATUS_ROW);
            Console.Write(new String(' ', 42)); // Length of the initial string we print on the console..
            Console.SetCursorPosition(0, STATUS_ROW);
            Console.Write(msg);
            Console.SetCursorPosition(maxRow, maxCol);
        }

        public ConsoleWriter()
        {
            displayPosition = new Dictionary<string, (int, int)>();
            displayLength = new Dictionary<string, int>();
            knownProvidersRowNum = new Dictionary<string, int>();
            unknownProvidersRowNum = new Dictionary<string, int>();

            int maxNameWidth = -1;
            foreach(CounterProvider provider in KnownData.GetAllProviders())
            {
                foreach(CounterProfile counterProfile in provider.GetAllCounters())
                {
                    if (counterProfile.DisplayName.Length > maxNameWidth)
                    {
                        maxNameWidth = counterProfile.DisplayName.Length;
                    }
                }
                knownProvidersRowNum[provider.Name] = -1;
            }
            leftAlign = maxNameWidth + 15;
        }

        public void InitializeDisplay()
        {
            Console.Clear();
            origRow = Console.CursorTop;
            origCol = Console.CursorLeft;
            Console.WriteLine("Press p to pause, r to resume, q to quit.");
            Console.WriteLine("    Status: Waiting for initial payload...");

            STATUS_ROW = origRow+1;
            maxRow = origRow+2;
            maxCol = origCol;
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

        // Generates a string using providerName and counterName that can be used as a dictionary key to prevent key collision
        private string CounterNameString(string providerName, string counterName)
        {
            return $"{providerName}:{counterName}";
        }

        public void Update(string providerName, ICounterPayload payload, bool pauseCmdSet)
        {

            if (!initialized)
            {
                initialized = true;
                UpdateStatus("    Status: Running");
            }

            if (pauseCmdSet)
            {
                return;
            }
            string name = payload.GetName();
            string keyName = CounterNameString(providerName, name);
            const string indent = "    ";
            const int indentLength = 4;
            // We already know what this counter is! Just update the value string on the console.
            if (displayPosition.ContainsKey(keyName))
            {
                (int left, int row) = displayPosition[keyName];
                string payloadVal = payload.GetValue();

                int clearLength = Math.Max(displayLength[keyName], payloadVal.Length); // Compute how long we need to clear out.
                displayLength[keyName] = clearLength;
                Console.SetCursorPosition(left, row); 
                Console.Write(new String(' ', clearLength));

                if (left < leftAlign)
                {
                    displayPosition[keyName] = (leftAlign, row);
                    Console.SetCursorPosition(leftAlign, row);
                }
                else
                {
                    Console.SetCursorPosition(left, row);
                }
                Console.Write(payloadVal);
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
                        Console.WriteLine($"[{providerName}]");
                        maxRow += 1;
                    }

                    KnownData.TryGetProvider(providerName, out CounterProvider counterProvider);
                    string displayName = counterProvider.TryGetDisplayName(name);
                    
                    if (displayName == null)
                    {
                        displayName = payload.GetDisplay();
                    }
                    
                    int left = displayName.Length;
                    string spaces = new String(' ', leftAlign-left-indentLength);
                    int row = maxRow;
                    string val = payload.GetValue();
                    displayPosition[keyName] = (leftAlign, row); 
                    displayLength[keyName] = val.Length;
                    Console.WriteLine($"{indent}{displayName}{spaces}{val}");
                    maxRow += 1;
                }
                else
                {
                    // If it's from an unknown provider, just append it at the end.
                    if (!unknownProvidersRowNum.ContainsKey(providerName))
                    {
                        unknownProvidersRowNum[providerName] = maxRow + 1;
                        Console.SetCursorPosition(0, maxRow);
                        Console.WriteLine($"[{providerName}]");
                        maxRow += 1;
                    }

                    string displayName = payload.GetDisplay();
                    
                    if (string.IsNullOrEmpty(displayName))
                    {
                        displayName = payload.GetName();
                    }
                    
                    int left = displayName.Length;

                    // If counter name is exceeds position of counter values, move values to the right
                    if (left+indentLength+4 > leftAlign) // +4 so that the counter value does not start right where the counter name ends
                    {
                        leftAlign = left+indentLength+4;
                    }

                    string spaces = new String(' ', leftAlign-left-indentLength);
                    int row = maxRow;
                    string val = payload.GetValue();
                    displayPosition[keyName] = (leftAlign, row); 
                    displayLength[keyName] = val.Length;
                    Console.WriteLine($"{indent}{displayName}{spaces}{val}");
                    maxRow += 1;
                }
            }
        }
    }
}
