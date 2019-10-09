using System;
using System.Collections.Generic;

namespace dotnet_counters.Tests
{

	class CounterPayloadBuilder
	{
		public static List<ICounterPayload> GenerateCounterPayload(string name, int value, int numPayloads)
		{
			List<ICounterPayload> payloads = new List<ICounterPayload>();

			for (var i = 0; i < numPayloads; i++)
			{
				IDictionary<string, object> counterField = new Dictionary<string, object>();
				counterField["Name"] = name;
				counterField["Mean"] = value.ToString();
				counterField["DisplayName"] = name;
			}

			payloads.Add(new CounterPayload())
			


		}

	}
}
