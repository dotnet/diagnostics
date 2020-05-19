// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Microsoft.Diagnostics.Monitoring.RestServer.Converters
{
    public class IntegerOrHexJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(long) == objectType;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (typeof(long) == reader.ValueType)
            {
                return (long)reader.Value;
            }

            string stringValue = reader.Value as string;
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                throw new JsonSerializationException("Value cannot be null, empty, or whitespace.");
            }
            else if (stringValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(stringValue.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long result))
                {
                    throw new JsonSerializationException(FormattableString.Invariant($"The value '{stringValue}' is not a valid hex number."));
                }
                return result;
            }
            else
            {
                if (!long.TryParse(stringValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long result))
                {
                    throw new JsonSerializationException(FormattableString.Invariant($"The value '{stringValue}' is not a valid integer."));
                }
                return result;
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
