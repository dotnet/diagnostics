// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring.LogAnalytics
{
    internal sealed class LogAnalyticsLogger : ILogger
    {
        private sealed class EmptyScopes : IDisposable
        {
            public void Dispose() {}
        }


        public IDisposable BeginScope<TState>(TState state)
        {
            return new EmptyScopes();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            return;
        }
    }
}
