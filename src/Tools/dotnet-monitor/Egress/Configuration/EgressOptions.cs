// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress.Configuration
{
    /// <summary>
    /// Configuration options for specifying egress providers.
    /// </summary>
    internal class EgressOptions
    {
        public const string ConfigurationKey = "Egress";

        /// <summary>
        /// Mapping of egress provider names to egress provider implementations.
        /// </summary>
        public Dictionary<string, ConfiguredEgressProvider> Providers { get; }
            = new Dictionary<string, ConfiguredEgressProvider>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Mapping of keyed values, typically used for naming a secret such as a storage account
        /// key or shared access signature rather than embedding values directly in the egress provider options.
        /// </summary>
        public Dictionary<string, string> Properties { get; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
