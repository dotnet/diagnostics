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

        public override long RundownKeyword
        {
            get => _configurations.Select(c => c.RundownKeyword).Aggregate((x, y) => x | y);
            set => throw new NotSupportedException();
        }

        public override RetryStrategy RetryStrategy
        {
            get
            {
                RetryStrategy result = RetryStrategy.DropKeywordDropRundown;
                foreach (MonitoringSourceConfiguration configuration in _configurations)
                {
                    if (result == RetryStrategy.DoNotRetry)
                    {
                        // Nothing overrides DoNotRetry
                        return RetryStrategy.DoNotRetry;
                    }
                    else if (result == RetryStrategy.DropKeywordDropRundown)
                    {
                        if (configuration.RetryStrategy == RetryStrategy.DropKeywordKeepRundown)
                        {
                            // DropKeywordKeepRundown overrides DropKeywordDropRundown
                            result = RetryStrategy.DropKeywordKeepRundown;
                        }
                    }
                }
                return result;
            }
            set => throw new NotSupportedException();
        }
    }
}
