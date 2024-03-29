﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public class LogObject : IReadOnlyList<KeyValuePair<string, object>>, IStateWithTimestamp
    {
        public static readonly Func<object, Exception, string> Callback = (state, exception) => ((LogObject)state).ToString();

        private readonly string _formattedMessage;
        private List<KeyValuePair<string, object>> _items = new();

        public LogObject(JsonElement element, string formattedMessage = null)
        {
            foreach (JsonProperty item in element.EnumerateObject())
            {
                switch (item.Value.ValueKind)
                {
                    case JsonValueKind.Undefined:
                        break;
                    case JsonValueKind.Object:
                        break;
                    case JsonValueKind.Array:
                        break;
                    case JsonValueKind.String:
                        _items.Add(new KeyValuePair<string, object>(item.Name, item.Value.GetString()));
                        break;
                    case JsonValueKind.Number:
                        _items.Add(new KeyValuePair<string, object>(item.Name, item.Value.GetInt32()));
                        break;
                    case JsonValueKind.False:
                    case JsonValueKind.True:
                        _items.Add(new KeyValuePair<string, object>(item.Name, item.Value.GetBoolean()));
                        break;
                    case JsonValueKind.Null:
                        _items.Add(new KeyValuePair<string, object>(item.Name, null));
                        break;
                    default:
                        break;
                }
            }

            _formattedMessage = formattedMessage;
        }

        public KeyValuePair<string, object> this[int index] => _items[index];

        public int Count => _items.Count;

        public DateTime Timestamp { get; internal set; }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return _items.GetEnumerator();
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
