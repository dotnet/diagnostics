// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Globalization;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress
{
    internal static class LoggerExtensions
    {
        private const string ProviderOptionLogFormat = "Provider option: {0} = {1}";
        private const string StreamOptionLogFormat = "Stream option: {0} = {1}";

        public static void LogProviderOption(this ILogger logger, string name, bool value)
        {
            logger.LogProviderOption(name, value.ToString(CultureInfo.InvariantCulture));
        }

        public static void LogProviderOption(this ILogger logger, string name, Uri value)
        {
            logger.LogProviderOption(name, value.ToString());
        }

        public static void LogProviderOption(this ILogger logger, string name, string value, bool redact = false)
        {
            if (redact)
            {
                value = Redact(value);
            }

            logger?.LogDebug(ProviderOptionLogFormat, name, value);
        }

        public static void LogStreamOption(this ILogger logger, string name, string value, bool redact = false)
        {
            if (redact)
            {
                value = Redact(value);
            }

            logger?.LogDebug(StreamOptionLogFormat, name, value);
        }

        private static string Redact(string value)
        {
            return string.IsNullOrEmpty(value) ? value : "<REDACTED>";
        }
    }
}
