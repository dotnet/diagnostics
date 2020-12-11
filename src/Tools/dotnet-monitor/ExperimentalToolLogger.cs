// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    // FUTURE: This log message should be removed when dotnet-monitor is no longer an experimental tool
    internal class ExperimentalToolLogger
    {
        private const string ExperimentMessage = "WARNING: dotnet-monitor is experimental and is not intended for production environments yet.";
        private const string NoAuthMessage = "WARNING: Authentication has been disabled. This can pose a security risk and is not intended for production environments.";
        private const string InsecureAuthMessage = "WARNING: Authentication is enabled over insecure http transport. This can pose a security risk and is not intended for production environments.";

        private readonly ILogger<ExperimentalToolLogger> _logger;

        public ExperimentalToolLogger(ILogger<ExperimentalToolLogger> logger)
        {
            _logger = logger;
        }

        public void LogExperimentMessage()
        {
            _logger.LogWarning(ExperimentMessage);
        }

        public void LogNoAuthMessage()
        {
            _logger.LogWarning(NoAuthMessage);
        }

        public void LogInsecureAuthMessage()
        {
            _logger.LogWarning(InsecureAuthMessage);
        }

        public static void AddLogFilter(ILoggingBuilder builder)
        {
            builder.AddFilter(typeof(ExperimentalToolLogger).FullName, LogLevel.Warning);
        }
    }
}
