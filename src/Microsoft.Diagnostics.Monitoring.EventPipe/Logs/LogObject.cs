// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Buffers;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class LogObject : IReadOnlyList<KeyValuePair<string, object>>, IStateWithTimestamp
    {
        public static readonly Func<object, Exception, string> Callback = (state, exception) => ((LogObject)state).ToString();

        private readonly string _formattedMessage;
        private KeyValuePair<string, object>[] _items = new KeyValuePair<string, object>[8];

        public LogObject(JsonElement element, string formattedMessage = null)
        {
            int index = 0;

            foreach (JsonProperty item in element.EnumerateObject())
            {
                if (index >= _items.Length)
                {
                    KeyValuePair<string, object>[] newArray = new KeyValuePair<string, object>[_items.Length * 2];
                    _items.CopyTo(newArray, 0);
                    _items = newArray;
                }

                switch (item.Value.ValueKind)
                {
                    case JsonValueKind.Undefined:
                        break;
                    case JsonValueKind.Object:
                        break;
                    case JsonValueKind.Array:
                        break;
                    case JsonValueKind.String:
                        _items[index++] = new KeyValuePair<string, object>(item.Name, item.Value.GetString());
                        break;
                    case JsonValueKind.Number:
                        _items[index++] = new KeyValuePair<string, object>(item.Name, item.Value.GetInt32());
                        break;
                    case JsonValueKind.False:
                    case JsonValueKind.True:
                        _items[index++] = new KeyValuePair<string, object>(item.Name, item.Value.GetBoolean());
                        break;
                    case JsonValueKind.Null:
                        _items[index++] = new KeyValuePair<string, object>(item.Name, null);
                        break;
                    default:
                        break;
                }
            }

            _formattedMessage = formattedMessage;
            Count = index;
        }

        public KeyValuePair<string, object> this[int index] => _items[index];

        public int Count { get; private set; }

        public DateTime Timestamp { get; internal set; }

        internal ReadOnlySpan<KeyValuePair<string, object>> ToSpan()
            => new(_items, 0, Count);

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return _formattedMessage ?? string.Empty;
        }
    }
}
