// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal static class LoggingExtensions
    {
        private static readonly Action<ILogger, Exception> _requestFailed =
            LoggerMessage.Define(
                eventId: new EventId(1, "RequestFailed"),
                logLevel: LogLevel.Error,
                formatString: "Request failed.");

        private static readonly Action<ILogger, Exception> _requestCanceled =
            LoggerMessage.Define(
                eventId: new EventId(2, "RequestCanceled"),
                logLevel: LogLevel.Information,
                formatString: "Request canceled.");

        private static readonly Action<ILogger, Exception> _resolvedTargetProcess =
            LoggerMessage.Define(
                eventId: new EventId(3, "ResolvedTargetProcess"),
                logLevel: LogLevel.Debug,
                formatString: "Resolved target process.");

        private static readonly Action<ILogger, string, Exception> _egressedArtifact =
            LoggerMessage.Define<string>(
                eventId: new EventId(4, "EgressedArtifact"),
                logLevel: LogLevel.Information,
                formatString: "Egressed artifact to {location}");

        private static readonly Action<ILogger, Exception> _writtenToHttpStream =
            LoggerMessage.Define(
                eventId: new EventId(5, "WrittenToHttpStream"),
                logLevel: LogLevel.Information,
                formatString: "Written to HTTP stream.");

        public static void RequestFailed(this ILogger logger, Exception ex)
        {
            _requestFailed(logger, ex);
        }

        public static void RequestCanceled(this ILogger logger)
        {
            _requestCanceled(logger, null);
        }

        public static void ResolvedTargetProcess(this ILogger logger)
        {
            _resolvedTargetProcess(logger, null);
        }

        public static void EgressedArtifact(this ILogger logger, string location)
        {
            _egressedArtifact(logger, location, null);
        }

        public static void WrittenToHttpStream(this ILogger logger)
        {
            _writtenToHttpStream(logger, null);
        }
    }
}
