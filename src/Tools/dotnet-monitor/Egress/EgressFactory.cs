// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    internal abstract class EgressFactory
    {
        public EgressFactory(ILogger logger)
        {
            Logger = logger;
        }

        public abstract bool TryCreate(
            string providerName,
            IConfigurationSection providerSection,
            Dictionary<string, string> egressProperties,
            out ConfiguredEgressProvider provider);

        protected bool TryValidateOptions(object value, string providerName)
        {
            Logger.LogDebug("Provider '{0}': Validating options.", providerName);
            EgressProviderValidation validation = new EgressProviderValidation(providerName, Logger);
            return validation.TryValidate(value);
        }

        protected ILogger Logger { get; }
    }
}
