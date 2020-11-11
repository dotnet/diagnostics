// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tools.Monitor.Egress
{
    /// <summary>
    /// Base class for creating configured egress providers.
    /// </summary>
    internal abstract class EgressFactory
    {
        public EgressFactory(ILogger logger)
        {
            Logger = logger;
        }

        /// <summary>
        /// Attempts to create a <see cref="ConfiguredEgressProvider"/> from the provided <paramref name="providerSection"/>
        /// and <paramref name="egressProperties"/>.
        /// </summary>
        /// <param name="providerName">The name of the egress provider.</param>
        /// <param name="providerSection">The configuration section containing the provider options.</param>
        /// <param name="egressProperties">The mapping of egress properties.</param>
        /// <param name="provider">The created <see cref="ConfiguredEgressProvider"/>.</param>
        /// <returns>True if the provider was created; otherwise, false.</returns>
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
