// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    // Logger implementations have different ways of serializing log scopes. This class helps those loggers
    // serialize the scope information in the best way possible for each of the implementations. For example,
    // the console logger will only call ToString on the scope data, thus the data needs to be formatted appropriately
    // in the ToString method. Another example, the event log logger will check if the scope data impelements
    // IEnumerable<KeyValuePair<string, object>> and then formats each value from the enumeration; it will fallback
    // calling the ToString method otherwise.
    internal class KeyValueLogScope : IEnumerable<KeyValuePair<string, object>>
    {
        public IDictionary<string, object> Values =
            new Dictionary<string, object>();

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            return Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)Values).GetEnumerator();
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            foreach (var kvp in Values)
            {
                if (builder.Length > 0)
                {
                    builder.Append(" ");
                }
                builder.Append(kvp.Key);
                builder.Append(":");
                builder.Append(kvp.Value);
            }
            return builder.ToString();
        }
    }
}
