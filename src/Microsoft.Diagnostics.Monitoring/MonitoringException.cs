// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Monitoring
{
    internal class MonitoringException : Exception
    {
        public MonitoringException(string message) : base(message) { }

        public MonitoringException(string message, Exception innerException) : base(message, innerException) { }
    }
}
