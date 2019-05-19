using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Counters
{
    public class TsvWriter : IOutputWriter
    {
        Dictionary<string,string> _columnsValue = new Dictionary<string, string>();

        public void Update(string providerName, ICounterPayload payload)
        {
            string name = payload.GetName();
            string value = payload.GetValue();

            if (_columnsValue.TryAdd(name, value))
            {
                // setup header file?
            }

            int columnNumber = 0;
            foreach(KeyValuePair<string,string> keyValue in _columnsValue)
            {
                Console.Write(keyValue.Value);
                if (++columnNumber == _columnsValue.Count)
                {
                    Console.Write(Environment.NewLine);
                }
                else
                {
                    Console.Write('\t');
                }
            }
        }
    }
}
