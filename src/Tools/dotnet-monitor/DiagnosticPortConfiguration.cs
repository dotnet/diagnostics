// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Tools.Monitor
{
    public class DiagnosticPortConfiguration
    {
        public DiagnosticPortConnectionMode ConnectionMode { get; set; }

        public string EndpointName { get; set; }

        public int? MaxConnections { get; set; }
    }

    public enum DiagnosticPortConnectionMode
    {
        Connect,
        Listen
    }
}
