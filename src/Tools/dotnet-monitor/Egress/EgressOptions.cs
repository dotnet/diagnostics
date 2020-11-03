// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal class EgressOptions
    {
        public const string ConfigurationKey = "Egress";

        public Dictionary<string, ConfiguredEgressEndpoint> Endpoints { get; }
            = new Dictionary<string, ConfiguredEgressEndpoint>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Properties { get; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
