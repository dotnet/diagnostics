// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    public sealed class AggregateSourceConfiguration : MonitoringSourceConfiguration
    {
        private IList<MonitoringSourceConfiguration> _configurations;

        public AggregateSourceConfiguration(params MonitoringSourceConfiguration[] configurations)
        {
            _configurations = configurations;
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            // CONSIDER: Might have to deduplicate providers and merge them together.
            return _configurations.SelectMany(c => c.GetProviders()).ToList();
        }

        public override bool RequestRundown
        {
            get => _configurations.Any(c => c.RequestRundown);
            set => throw new NotSupportedException();
        }
    }
}
