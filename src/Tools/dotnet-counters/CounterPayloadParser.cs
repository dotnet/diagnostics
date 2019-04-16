using System;
using System.Collections.Generic;


namespace Microsoft.Diagnostics.Tools.Counters
{
	public class CounterPayloadParser
	{
		private Dictionary<string, CounterProvider> providerProfiles;
		private Dictionary<string, string> displayNames;

		public CounterPayloadParser()
		{
			providerProfiles = new Dictionary<string, CounterProvider>();
			displayNames = new Dictionary<string, string>();
			foreach (CounterProvider provider in KnownData.GetAllProviders())
            {
                providerProfiles.Add(provider.Name, provider);
            }
		}

		// Returns a tuple of CounterName, CounterValue
		public (string name, string val) ParseCounterValue(string providerName, string payload)
		{
			string[] payloadTokens = payload.Split(",");
            string name = payloadTokens[0].Split(":")[2];
            name = name.Substring(1, name.Length-2);  // This removes quotation marks around the name

            string displayName = payloadTokens[1].Split(":")[1];

            string val = GetCounterValue(providerName, name, payloadTokens);
            return (name, val);
		}

		public string GetDisplayName(string name)
		{
			if (displayNames.TryGetValue(name, out string displayName))
			{
				return displayName;
			}
			return name;
		}

        private string GetCounterValue(string providerName, string counterName, string[] payloadTokens)
        {
            // TODO: error handling
            if (!providerProfiles.TryGetValue(providerName, out CounterProvider provider))
            {
                Console.WriteLine("Couldn't find provider");
                return "-1"; // TODO: should we return sth else?
            }

            if (!provider.Counters.TryGetValue(counterName, out CounterProfile counterProfile))
            {
                Console.WriteLine($"Couldn't find counter: {counterName}");
                return "-1";
            }

            CounterType counterType = counterProfile.Type;

            switch (counterType)
            {
                case CounterType.EventCounter:
                    return payloadTokens[2].Split(":")[1];
                case CounterType.PollingCounter:
                    return payloadTokens[2].Split(":")[1];
                case CounterType.IncrementingPollingCounter:
                    return payloadTokens[3].Split(":")[1];
                case CounterType.IncrementingEventCounter:
                    return payloadTokens[3].Split(":")[1];
            }
            Console.WriteLine("Couldn't determine type");
            return "-1";
        }
	}
}