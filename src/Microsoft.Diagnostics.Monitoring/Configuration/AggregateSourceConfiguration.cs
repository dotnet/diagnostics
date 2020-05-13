// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class AggregateSourceConfiguration : MonitoringSourceConfiguration
    {
        private IList<MonitoringSourceConfiguration> _configurations = new List<MonitoringSourceConfiguration>();

        public void AddConfiguration(MonitoringSourceConfiguration configuration)
        {
            _configurations.Add(configuration);
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            return _configurations.SelectMany(c => c.GetProviders()).ToList();
        }
    }
}
