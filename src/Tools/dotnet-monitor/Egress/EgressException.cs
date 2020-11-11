// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using System;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress
{
    /// <summary>
    /// Exception that egress providers can throw when an operational error occurs (e.g. failed to write the stream data).
    /// </summary>
    internal class EgressException : MonitoringException
    {
        public EgressException(string message) : base(message) { }

        public EgressException(string message, Exception innerException) : base(message, innerException) { }
    }
}
