﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring
{
    public sealed class EventPipeProviderSourceConfiguration : MonitoringSourceConfiguration
    {
        private readonly IEnumerable<EventPipeProvider> _providers;
        private readonly bool _requestRundown;
        private readonly int _bufferSizeInMB;

        public EventPipeProviderSourceConfiguration(bool requestRundown = true, int bufferSizeInMB = 256, params EventPipeProvider[] providers)
        {
            _providers = providers;
            _requestRundown = requestRundown;
            _bufferSizeInMB = bufferSizeInMB;
        }

        public override IList<EventPipeProvider> GetProviders()
        {
            return _providers.ToList();
        }

        public override bool RequestRundown => _requestRundown;

        public override int BufferSizeInMB => _bufferSizeInMB;
    }
}
