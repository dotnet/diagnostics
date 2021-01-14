// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    // Logger implementations have different ways of serializing log scopes. This class helps those loggers
    // serialize the scope information in the best way possible for each of the implementations.
    //
    // Handled examples:
    // - Simple Console Logger: only calls ToString, thus the data needs to be formatted in the ToString method.
    // - JSON Console Logger: checks for IReadOnlyCollection<KeyValuePair<string, object>> and formats each value
    //   in the enumeration; otherwise falls back to ToString.
    // - Event Log Logger: checks for IEnumerable<KeyValuePair<string, object>> and formats each value
    //   in the enumeration; otherwise falls back to ToString.
    internal class KeyValueLogScope : IReadOnlyCollection<KeyValuePair<string, object>>
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

        int IReadOnlyCollection<KeyValuePair<string, object>>.Count => Values.Count;

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
