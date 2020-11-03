// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal abstract class EgressProvider
    {
        public abstract bool TryParse(
            string endpointName,
            IConfigurationSection endpointSection,
            Dictionary<string, string> egressProperties,
            out ConfiguredEgressEndpoint endpoint);
    }
}
