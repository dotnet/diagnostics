// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Tools.RuntimeClient;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class CounterValueHolder
    {
        private SortedDictionary<string, ProviderValueHolder> providerCounterValues;  // Provider => ProviderValueHolder

        public CounterValueHolder()
        {
            // TODO: Only get the enabled ones
            providerCounterValues = new SortedDictionary<string, ProviderValueHolder>();

            foreach (CounterProvider provider in KnownData.GetAllProviders())
            {
                providerCounterValues.Add(provider.Name, new ProviderValueHolder(provider));
            }
        }

        public void Update(string providerName, ICounterPayload payload)
        {
            providerCounterValues[providerName].Update(payload);
        }
    }

    // Represents all the counters in a particular provider (i.e. System.Runtime) 
    public class ProviderValueHolder
    {
    	private SortedDictionary<string, string> counterValues;
        private Dictionary<string, string> nameToDisplayName;

        public ProviderValueHolder(CounterProvider provider)
        {
            counterValues = new SortedDictionary<string, string>();
            nameToDisplayName = new Dictionary<string, string>();
        }

        public int counterCount()
        {
            return counterValues.Keys.Count;
        }

        private string ToDisplayName(string name)
        {
            if (nameToDisplayName.TryGetValue(name, out string displayName))
            {
                return displayName;
            }
            return name;
        }

        public void Update(ICounterPayload payload)
        {
            counterValues[payload.GetName()] = payload.GetValue();

            if (nameToDisplayName.ContainsKey(payload.GetName()))
            {
                nameToDisplayName[payload.GetName()] = payload.GetDisplay();
            }
        }

        // Displays all the counters in this Provider.
        // Takes in an int parameter that represents how many lines have been printed above this provider's counters.
        public void InitializeDisplay(int topLines)
        {
            int top = topLines;
            foreach (KeyValuePair<string, string> kvPair in counterValues)
            {
                string dpName = ToDisplayName(kvPair.Key);
                Console.WriteLine($"    {dpName} : {kvPair.Value}");
                top += 1;
            }
        }
    }
}
